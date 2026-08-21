using System;
using System.Globalization;
using System.Text.Json;

namespace CarpaNet.Jetstream;

/// <summary>
/// How a decoded live frame should be handled by the session loop.
/// </summary>
internal enum JetstreamV2FrameKind
{
    /// <summary>A decoded event; <see cref="JetstreamV2FrameResult.Event"/> is non-null.</summary>
    Event,

    /// <summary>An #info advisory (no seq, not an event): log and continue.</summary>
    Info,

    /// <summary>A terminal error frame; the server closes right after sending it.</summary>
    StreamError,

    /// <summary>A well-formed frame from a newer protocol revision: skip for forward compat.</summary>
    Skip,

    /// <summary>A malformed frame: surface (log) but keep the connection.</summary>
    Malformed,
}

/// <summary>
/// The outcome of decoding one xrpc.v1.json frame.
/// </summary>
internal sealed class JetstreamV2FrameResult
{
    public JetstreamV2FrameKind Kind { get; private set; }

    public JetstreamV2Event? Event { get; private set; }

    /// <summary>Info name, stream error name, or malformed-frame description depending on <see cref="Kind"/>.</summary>
    public string? Name { get; private set; }

    public string? Message { get; private set; }

    public static JetstreamV2FrameResult ForEvent(JetstreamV2Event evt) =>
        new() { Kind = JetstreamV2FrameKind.Event, Event = evt };

    public static JetstreamV2FrameResult ForInfo(string name, string? message) =>
        new() { Kind = JetstreamV2FrameKind.Info, Name = name, Message = message };

    public static JetstreamV2FrameResult ForStreamError(string name, string? message) =>
        new() { Kind = JetstreamV2FrameKind.StreamError, Name = name, Message = message };

    public static JetstreamV2FrameResult ForSkip() =>
        new() { Kind = JetstreamV2FrameKind.Skip };

    public static JetstreamV2FrameResult ForMalformed(string reason) =>
        new() { Kind = JetstreamV2FrameKind.Malformed, Name = reason };
}

/// <summary>
/// Decodes xrpc.v1.json frames from the network.bsky.jetstream.subscribeEvents wire into
/// <see cref="JetstreamV2Event"/> values. Unknown envelope or payload $types skip for forward
/// compatibility; a frame with no $type at all is malformed (it usually means the client hit
/// a v1 /subscribe endpoint).
/// </summary>
internal static class JetstreamV2FrameDecoder
{
    private const string PayloadTypePrefix = "network.bsky.jetstream.subscribeEvents#";

    // Bounds on untrusted server-supplied diagnostic strings before they enter
    // exception messages and logs.
    private const int MaxDiagNameLength = 128;
    private const int MaxDiagMessageLength = 1024;

    private const long UnixEpochTicks = 621355968000000000L;

    public static JetstreamV2FrameResult Decode(ReadOnlySpan<byte> frame)
    {
        JetstreamV2Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(frame, JetstreamV2JsonContext.Default.JetstreamV2Envelope);
        }
        catch (JsonException ex)
        {
            return JetstreamV2FrameResult.ForMalformed($"invalid frame JSON: {ex.Message}");
        }

        if (envelope == null)
        {
            return JetstreamV2FrameResult.ForMalformed("null frame");
        }

        switch (envelope.Type)
        {
            case "message":
                break;
            case "error":
                if (string.IsNullOrEmpty(envelope.Error))
                {
                    return JetstreamV2FrameResult.ForMalformed("error frame missing error code");
                }

                return JetstreamV2FrameResult.ForStreamError(
                    Bound(envelope.Error!, MaxDiagNameLength),
                    envelope.Message == null ? null : Bound(envelope.Message, MaxDiagMessageLength));
            case null:
            case "":
                // No $type at all is not a newer protocol revision — it is a malformed frame
                // (e.g. a v1 /subscribe server). Skipping it would make a wrong endpoint look
                // healthy while delivering nothing.
                return JetstreamV2FrameResult.ForMalformed(
                    "frame missing envelope $type; is the server a network.bsky.jetstream.subscribeEvents endpoint?");
            default:
                // A well-formed frame with an unknown envelope $type is a newer protocol
                // revision; skip rather than break.
                return JetstreamV2FrameResult.ForSkip();
        }

        if (envelope.Payload == null || envelope.Payload.Value.ValueKind != JsonValueKind.Object)
        {
            return JetstreamV2FrameResult.ForMalformed("message frame missing payload");
        }

        var payload = envelope.Payload.Value;
        if (!payload.TryGetProperty("$type", out var typeProperty) || typeProperty.ValueKind != JsonValueKind.String)
        {
            // A payload with NO $type is malformed, not a future addition — skipping it would
            // be silent event loss.
            return JetstreamV2FrameResult.ForMalformed("message payload missing $type");
        }

        var payloadType = typeProperty.GetString() ?? string.Empty;
        return payloadType switch
        {
            PayloadTypePrefix + "commit" => DecodeCommit(payload),
            PayloadTypePrefix + "identity" => DecodeIdentity(payload),
            PayloadTypePrefix + "account" => DecodeAccount(payload),
            PayloadTypePrefix + "sync" => DecodeSync(payload),
            PayloadTypePrefix + "info" => DecodeInfo(payload),
            // A nonempty unknown $type is a newer server's message kind; skip for forward compat.
            _ => JetstreamV2FrameResult.ForSkip(),
        };
    }

    private static JetstreamV2FrameResult DecodeCommit(JsonElement payload)
    {
        JetstreamV2CommitPayload? commit;
        try
        {
            commit = payload.Deserialize(JetstreamV2JsonContext.Default.JetstreamV2CommitPayload);
        }
        catch (JsonException ex)
        {
            return JetstreamV2FrameResult.ForMalformed($"invalid commit payload: {ex.Message}");
        }

        if (commit == null)
        {
            return JetstreamV2FrameResult.ForMalformed("null commit payload");
        }

        if (!TryEnvelopeFields(commit.Seq, commit.Time, out var timeUs, out var reason))
        {
            return JetstreamV2FrameResult.ForMalformed(reason);
        }

        // All four identifiers are lexicon-required, and a folding consumer cannot key a
        // mutation without them; a frame omitting one must error rather than emit an
        // unfoldable event that advances the dedup cursor.
        if (string.IsNullOrEmpty(commit.Did) || string.IsNullOrEmpty(commit.Rev) ||
            string.IsNullOrEmpty(commit.Collection) || string.IsNullOrEmpty(commit.Rkey))
        {
            return JetstreamV2FrameResult.ForMalformed("commit frame missing required did, rev, collection, or rkey");
        }

        JetstreamV2CommitOperation operation;
        switch (commit.Operation)
        {
            case "create":
                operation = JetstreamV2CommitOperation.Create;
                break;
            case "update":
                operation = JetstreamV2CommitOperation.Update;
                break;
            case "delete":
                operation = JetstreamV2CommitOperation.Delete;
                break;
            default:
                return JetstreamV2FrameResult.ForMalformed($"unknown commit operation \"{commit.Operation}\"");
        }

        var result = new JetstreamV2Commit
        {
            Operation = operation,
            Collection = commit.Collection!,
            Rkey = commit.Rkey!,
            Rev = commit.Rev!,
            Cid = commit.Cid,
        };

        if (operation != JetstreamV2CommitOperation.Delete)
        {
            if (commit.Record == null || commit.Record.Value.ValueKind != JsonValueKind.Object)
            {
                return JetstreamV2FrameResult.ForMalformed(
                    $"{commit.Operation} commit missing record (collection={commit.Collection} rkey={commit.Rkey})");
            }

            result.Record = commit.Record;
        }

        return JetstreamV2FrameResult.ForEvent(new JetstreamV2Event
        {
            Did = commit.Did!,
            Seq = commit.Seq,
            TimeUs = timeUs,
            Kind = JetstreamV2EventKind.Commit,
            Commit = result,
        });
    }

    private static JetstreamV2FrameResult DecodeIdentity(JsonElement payload)
    {
        JetstreamV2IdentityPayload? identity;
        try
        {
            identity = payload.Deserialize(JetstreamV2JsonContext.Default.JetstreamV2IdentityPayload);
        }
        catch (JsonException ex)
        {
            return JetstreamV2FrameResult.ForMalformed($"invalid identity payload: {ex.Message}");
        }

        // Presence-of-payload check: the outer DID is lexicon-required and is the Event.Did
        // every filter and fold keys on; the wrapped upstream event must at least carry a DID.
        if (identity == null || string.IsNullOrEmpty(identity.Did) ||
            identity.Identity == null || string.IsNullOrEmpty(identity.Identity.Did))
        {
            return JetstreamV2FrameResult.ForMalformed("identity frame missing required DID or identity payload");
        }

        if (!TryEnvelopeFields(identity.Seq, identity.Time, out var timeUs, out var reason))
        {
            return JetstreamV2FrameResult.ForMalformed(reason);
        }

        return JetstreamV2FrameResult.ForEvent(new JetstreamV2Event
        {
            Did = identity.Did!,
            Seq = identity.Seq,
            TimeUs = timeUs,
            Kind = JetstreamV2EventKind.Identity,
            Identity = new JetstreamV2Identity
            {
                Did = identity.Identity.Did!,
                Handle = string.IsNullOrEmpty(identity.Identity.Handle) ? null : identity.Identity.Handle,
                Seq = identity.Identity.Seq,
                Time = identity.Identity.Time ?? string.Empty,
            },
        });
    }

    private static JetstreamV2FrameResult DecodeAccount(JsonElement payload)
    {
        JetstreamV2AccountPayload? account;
        try
        {
            account = payload.Deserialize(JetstreamV2JsonContext.Default.JetstreamV2AccountPayload);
        }
        catch (JsonException ex)
        {
            return JetstreamV2FrameResult.ForMalformed($"invalid account payload: {ex.Message}");
        }

        if (account == null || string.IsNullOrEmpty(account.Did) ||
            account.Account == null || string.IsNullOrEmpty(account.Account.Did))
        {
            return JetstreamV2FrameResult.ForMalformed("account frame missing required DID or account payload");
        }

        if (!TryEnvelopeFields(account.Seq, account.Time, out var timeUs, out var reason))
        {
            return JetstreamV2FrameResult.ForMalformed(reason);
        }

        return JetstreamV2FrameResult.ForEvent(new JetstreamV2Event
        {
            Did = account.Did!,
            Seq = account.Seq,
            TimeUs = timeUs,
            Kind = JetstreamV2EventKind.Account,
            Account = new JetstreamV2Account
            {
                Did = account.Account.Did!,
                Active = account.Account.Active,
                Status = string.IsNullOrEmpty(account.Account.Status) ? null : account.Account.Status,
                Seq = account.Account.Seq,
                Time = account.Account.Time ?? string.Empty,
            },
        });
    }

    private static JetstreamV2FrameResult DecodeSync(JsonElement payload)
    {
        JetstreamV2SyncPayload? sync;
        try
        {
            sync = payload.Deserialize(JetstreamV2JsonContext.Default.JetstreamV2SyncPayload);
        }
        catch (JsonException ex)
        {
            return JetstreamV2FrameResult.ForMalformed($"invalid sync payload: {ex.Message}");
        }

        // Archived #sync payloads from an async resync legitimately carry an empty time/seq,
        // so did is the only reliable presence marker.
        if (sync == null || string.IsNullOrEmpty(sync.Did) ||
            sync.Sync == null || string.IsNullOrEmpty(sync.Sync.Did))
        {
            return JetstreamV2FrameResult.ForMalformed("sync frame missing required DID or sync payload");
        }

        if (!TryEnvelopeFields(sync.Seq, sync.Time, out var timeUs, out var reason))
        {
            return JetstreamV2FrameResult.ForMalformed(reason);
        }

        return JetstreamV2FrameResult.ForEvent(new JetstreamV2Event
        {
            Did = sync.Did!,
            Seq = sync.Seq,
            TimeUs = timeUs,
            Kind = JetstreamV2EventKind.Sync,
            Sync = new JetstreamV2Sync
            {
                Did = sync.Sync.Did!,
                Rev = sync.Sync.Rev ?? string.Empty,
                Seq = sync.Sync.Seq,
                Time = sync.Sync.Time ?? string.Empty,
            },
        });
    }

    private static JetstreamV2FrameResult DecodeInfo(JsonElement payload)
    {
        JetstreamV2InfoPayload? info;
        try
        {
            info = payload.Deserialize(JetstreamV2JsonContext.Default.JetstreamV2InfoPayload);
        }
        catch (JsonException ex)
        {
            return JetstreamV2FrameResult.ForMalformed($"invalid info payload: {ex.Message}");
        }

        return JetstreamV2FrameResult.ForInfo(
            Bound(info?.Name ?? string.Empty, MaxDiagNameLength),
            info?.Message == null ? null : Bound(info.Message, MaxDiagMessageLength));
    }

    /// <summary>
    /// Validates the envelope fields shared by every message kind: seq (1-based on the wire,
    /// so 0 means the required field was absent) and the canonical microsecond-precision
    /// datetime, parsed back to unix microseconds.
    /// </summary>
    private static bool TryEnvelopeFields(long seq, string? time, out long timeUs, out string reason)
    {
        timeUs = 0;
        if (seq <= 0)
        {
            reason = $"frame with invalid seq {seq}";
            return false;
        }

        if (string.IsNullOrEmpty(time) ||
            !DateTimeOffset.TryParse(
                time,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            reason = $"frame time \"{time}\" is not a valid datetime";
            return false;
        }

        timeUs = (parsed.UtcTicks - UnixEpochTicks) / 10;
        reason = string.Empty;
        return true;
    }

    private static string Bound(string value, int limit)
    {
        if (value.Length <= limit)
        {
            return value;
        }

        return value.Substring(0, limit) + "…";
    }
}
