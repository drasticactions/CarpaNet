using System;
using System.Formats.Cbor;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CarpaNet.Cbor;

namespace CarpaNet.Jetstream;

/// <summary>
/// Converts decoded sealed-segment rows into <see cref="JetstreamV2Event"/> values: commit
/// payloads are DAG-CBOR records converted to the atproto JSON data model (the same shape the
/// live wire carries in "record"), and marker rows wrap the upstream
/// com.atproto.sync.subscribeRepos event's DAG-CBOR.
/// </summary>
internal static class JetstreamV2RecordDecoder
{
    /// <summary>
    /// Converts one archive row into the public event shape. Throws
    /// <see cref="JetstreamV2Exception"/> for a semantically malformed row; the caller logs
    /// and skips it so one bad upstream record does not lose the rest of the block.
    /// </summary>
    public static JetstreamV2Event ConvertRow(JetstreamSegmentRow row)
    {
        var evt = new JetstreamV2Event
        {
            Did = row.Did,
            Seq = row.Seq,
            TimeUs = row.DisplayTimeUs,
        };

        switch (row.Kind)
        {
            case JetstreamSegmentRowKind.Create:
            case JetstreamSegmentRowKind.Update:
            case JetstreamSegmentRowKind.Delete:
            case JetstreamSegmentRowKind.CreateResync:
                evt.Kind = JetstreamV2EventKind.Commit;
                evt.Commit = ConvertCommit(row);
                break;
            case JetstreamSegmentRowKind.Identity:
                evt.Kind = JetstreamV2EventKind.Identity;
                evt.Identity = DecodeIdentity(row);
                break;
            case JetstreamSegmentRowKind.Account:
                evt.Kind = JetstreamV2EventKind.Account;
                evt.Account = DecodeAccount(row);
                break;
            case JetstreamSegmentRowKind.Sync:
                evt.Kind = JetstreamV2EventKind.Sync;
                evt.Sync = DecodeSync(row);
                break;
            default:
                throw new JetstreamV2Exception($"unknown segment row kind {(int)row.Kind} (did={row.Did} seq={row.Seq})");
        }

        return evt;
    }

    private static JetstreamV2Commit ConvertCommit(JetstreamSegmentRow row)
    {
        var commit = new JetstreamV2Commit
        {
            Operation = row.Kind switch
            {
                JetstreamSegmentRowKind.Update => JetstreamV2CommitOperation.Update,
                JetstreamSegmentRowKind.Delete => JetstreamV2CommitOperation.Delete,
                _ => JetstreamV2CommitOperation.Create,
            },
            Collection = row.Collection,
            Rkey = row.Rkey,
            Rev = row.Rev,
        };

        if (row.Kind == JetstreamSegmentRowKind.Delete)
        {
            return commit;
        }

        if (row.Payload == null || row.Payload.Length == 0)
        {
            throw new JetstreamV2Exception(
                $"archive commit missing record payload (did={row.Did} collection={row.Collection} rkey={row.Rkey} seq={row.Seq})");
        }

        try
        {
            commit.Record = CborToJsonElement(row.Payload);
        }
        catch (Exception ex) when (ex is not JetstreamV2Exception)
        {
            throw new JetstreamV2Exception(
                $"decode record (did={row.Did} collection={row.Collection} rkey={row.Rkey} seq={row.Seq}): {ex.Message}", ex);
        }

        commit.Cid = ComputeCid(row.Payload);
        return commit;
    }

    /// <summary>
    /// Computes the record's content identifier: the ATProto blessed CIDv1
    /// (dag-cbor, sha2-256, base32lower) over the raw DAG-CBOR payload.
    /// </summary>
    internal static string ComputeCid(byte[] payload)
    {
        using var sha = SHA256.Create();
        return ATCid.FromSha256Hash(sha.ComputeHash(payload)).Value;
    }

    /// <summary>
    /// Converts DAG-CBOR record bytes into the atproto JSON data model: integers stay JSON
    /// numbers, byte strings become <c>{"$bytes": base64-without-padding}</c>, CID links become
    /// <c>{"$link": cid}</c>. Floats are rejected — the atproto data model has integers, not
    /// floats — and the top-level value must be a map.
    /// </summary>
    internal static JsonElement CborToJsonElement(byte[] payload)
    {
        var reader = new DagCborReader(payload);
        if (reader.PeekState() != CborReaderState.StartMap)
        {
            throw new JetstreamV2Exception("record is not a CBOR map");
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteValue(ref reader, writer);
        }

        if (reader.BytesRemaining != 0)
        {
            throw new JetstreamV2Exception("record has trailing bytes after the CBOR value");
        }

        stream.Position = 0;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static void WriteValue(ref DagCborReader reader, Utf8JsonWriter writer)
    {
        switch (reader.PeekState())
        {
            case CborReaderState.Null:
                reader.ReadNull();
                writer.WriteNullValue();
                break;
            case CborReaderState.Boolean:
                writer.WriteBooleanValue(reader.ReadBoolean());
                break;
            case CborReaderState.UnsignedInteger:
            case CborReaderState.NegativeInteger:
                writer.WriteNumberValue(reader.ReadInt64());
                break;
            case CborReaderState.TextString:
                writer.WriteStringValue(reader.ReadTextString());
                break;
            case CborReaderState.ByteString:
                writer.WriteStartObject();
                writer.WriteString("$bytes", ToBase64NoPadding(reader.ReadByteString()));
                writer.WriteEndObject();
                break;
            case CborReaderState.Tag:
                var cid = reader.ReadCidLink();
                writer.WriteStartObject();
                writer.WriteString("$link", cid.Value);
                writer.WriteEndObject();
                break;
            case CborReaderState.StartArray:
                writer.WriteStartArray();
                var arrayCount = reader.ReadStartArray();
                var remainingItems = arrayCount ?? int.MaxValue;
                while (remainingItems > 0 && reader.PeekState() != CborReaderState.EndArray)
                {
                    WriteValue(ref reader, writer);
                    remainingItems--;
                }

                reader.ReadEndArray();
                writer.WriteEndArray();
                break;
            case CborReaderState.StartMap:
                writer.WriteStartObject();
                var mapCount = reader.ReadStartMap();
                var remainingPairs = mapCount ?? int.MaxValue;
                while (remainingPairs > 0 && reader.PeekState() != CborReaderState.EndMap)
                {
                    writer.WritePropertyName(reader.ReadTextString());
                    WriteValue(ref reader, writer);
                    remainingPairs--;
                }

                reader.ReadEndMap();
                writer.WriteEndObject();
                break;
            default:
                // Floats and anything else are outside the atproto data model.
                throw new JetstreamV2Exception($"record contains a value outside the atproto data model ({reader.PeekState()})");
        }
    }

    private static string ToBase64NoPadding(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=');

    private static JetstreamV2Identity DecodeIdentity(JetstreamSegmentRow row)
    {
        var fields = DecodeUpstreamFields(row, "identity");
        return new JetstreamV2Identity
        {
            Did = fields.Did ?? row.Did,
            Handle = fields.Handle,
            Seq = fields.Seq,
            Time = fields.Time ?? string.Empty,
        };
    }

    private static JetstreamV2Account DecodeAccount(JetstreamSegmentRow row)
    {
        var fields = DecodeUpstreamFields(row, "account");
        return new JetstreamV2Account
        {
            Did = fields.Did ?? row.Did,
            Active = fields.Active,
            Status = fields.Status,
            Seq = fields.Seq,
            Time = fields.Time ?? string.Empty,
        };
    }

    private static JetstreamV2Sync DecodeSync(JetstreamSegmentRow row)
    {
        var fields = DecodeUpstreamFields(row, "sync");
        return new JetstreamV2Sync
        {
            Did = fields.Did ?? row.Did,
            Rev = fields.Rev ?? string.Empty,
            Seq = fields.Seq,
            Time = fields.Time ?? string.Empty,
        };
    }

    private struct UpstreamFields
    {
        public string? Did;
        public string? Handle;
        public string? Status;
        public string? Rev;
        public string? Time;
        public long Seq;
        public bool Active;
    }

    /// <summary>
    /// Decodes the payload of a marker row: the upstream com.atproto.sync.subscribeRepos
    /// event as a DAG-CBOR map. Unknown keys (e.g. a #sync event's raw "blocks" bytes) are
    /// skipped for forward compatibility.
    /// </summary>
    private static UpstreamFields DecodeUpstreamFields(JetstreamSegmentRow row, string kindName)
    {
        if (row.Payload == null || row.Payload.Length == 0)
        {
            throw new JetstreamV2Exception($"archive {kindName} row missing payload (did={row.Did} seq={row.Seq})");
        }

        var fields = default(UpstreamFields);
        try
        {
            var reader = new DagCborReader(row.Payload);
            var pairCount = reader.ReadStartMap();
            var remaining = pairCount ?? int.MaxValue;
            while (remaining > 0 && reader.PeekState() != CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                remaining--;
                if (reader.PeekState() == CborReaderState.Null)
                {
                    reader.ReadNull();
                    continue;
                }

                switch (key)
                {
                    case "did":
                        fields.Did = reader.ReadTextString();
                        break;
                    case "handle":
                        fields.Handle = reader.ReadTextString();
                        break;
                    case "status":
                        fields.Status = reader.ReadTextString();
                        break;
                    case "rev":
                        fields.Rev = reader.ReadTextString();
                        break;
                    case "time":
                        fields.Time = reader.ReadTextString();
                        break;
                    case "seq":
                        fields.Seq = reader.ReadInt64();
                        break;
                    case "active":
                        fields.Active = reader.ReadBoolean();
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }

            reader.ReadEndMap();
        }
        catch (Exception ex) when (ex is not JetstreamV2Exception)
        {
            throw new JetstreamV2Exception($"decode archive {kindName} payload (did={row.Did} seq={row.Seq}): {ex.Message}", ex);
        }

        return fields;
    }
}
