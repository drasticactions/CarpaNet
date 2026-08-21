using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CarpaNet.Jetstream;
using Xunit;
using ZstdSharp;

namespace CarpaNet.UnitTests.Jetstream;

/// <summary>
/// Encodes the sealed-segment wire format from the writer's side (a port of the reference
/// implementation's columnar layout) so the decoder can be exercised without fixture files.
/// </summary>
internal static class JetstreamSegmentTestData
{
    public static byte[] EncodeBlockBody(IReadOnlyList<JetstreamSegmentRow> rows)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)rows.Count);

        foreach (var row in rows)
        {
            writer.Write((ulong)row.Seq);
        }

        foreach (var row in rows)
        {
            writer.Write((ulong)row.WitnessedAt);
        }

        foreach (var row in rows)
        {
            writer.Write((ulong)row.IndexedAt);
        }

        foreach (var row in rows)
        {
            writer.Write((byte)row.Kind);
        }

        foreach (var row in rows)
        {
            writer.Write((byte)Encoding.UTF8.GetByteCount(row.Collection));
        }

        foreach (var row in rows)
        {
            writer.Write((ushort)Encoding.UTF8.GetByteCount(row.Did));
        }

        foreach (var row in rows)
        {
            writer.Write((byte)Encoding.UTF8.GetByteCount(row.Rkey));
        }

        foreach (var row in rows)
        {
            writer.Write((byte)Encoding.UTF8.GetByteCount(row.Rev));
        }

        foreach (var row in rows)
        {
            writer.Write((uint)(row.Payload?.Length ?? 0));
        }

        foreach (var row in rows)
        {
            writer.Write(Encoding.UTF8.GetBytes(row.Collection));
        }

        foreach (var row in rows)
        {
            writer.Write(Encoding.UTF8.GetBytes(row.Did));
        }

        foreach (var row in rows)
        {
            writer.Write(Encoding.UTF8.GetBytes(row.Rkey));
        }

        foreach (var row in rows)
        {
            writer.Write(Encoding.UTF8.GetBytes(row.Rev));
        }

        foreach (var row in rows)
        {
            if (row.Payload != null)
            {
                writer.Write(row.Payload);
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] CompressFrame(byte[] body)
    {
        using var compressor = new Compressor();
        return compressor.Wrap(body).ToArray();
    }

    public static byte[] EncodeBlockFrame(IReadOnlyList<JetstreamSegmentRow> rows) =>
        CompressFrame(EncodeBlockBody(rows));

    /// <summary>
    /// Builds a minimal but structurally valid sealed segment file: fixed header, 8-byte
    /// length-prefixed block frames, and the 52-byte-entry block index at the footer offset.
    /// </summary>
    public static byte[] BuildSegmentFile(params IReadOnlyList<JetstreamSegmentRow>[] blocks)
    {
        var frames = blocks.Select(EncodeBlockFrame).ToArray();
        var dataLength = frames.Sum(f => f.Length + 8);
        var footerOffset = JetstreamSegmentFormat.HeaderSize + dataLength;
        var file = new byte[footerOffset + blocks.Length * 52];
        var span = file.AsSpan();

        // Header.
        span[0] = (byte)'j';
        span[1] = (byte)'s';
        span[2] = (byte)'s';
        span[3] = (byte)'0';
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(4, 8), 0xDEADBEEF); // non-zero = sealed
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(12, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(14, 4), (uint)blocks.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(18, 4), (uint)blocks.Sum(b => b.Count));
        var allRows = blocks.SelectMany(b => b).ToList();
        if (allRows.Count > 0)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(26, 8), (ulong)allRows.Min(r => r.Seq));
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(34, 8), (ulong)allRows.Max(r => r.Seq));
        }

        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(58, 8), (ulong)footerOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(90, 8), (ulong)footerOffset); // block index

        // Data region: length-prefixed frames.
        var offset = JetstreamSegmentFormat.HeaderSize;
        for (var i = 0; i < frames.Length; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(offset, 8), (ulong)frames[i].Length);
            frames[i].CopyTo(span.Slice(offset + 8));

            // Block index entry: frame offset (before the length prefix) + compressed size.
            var entry = span.Slice(footerOffset + i * 52, 52);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(0, 8), (ulong)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(8, 4), (uint)frames[i].Length);

            offset += 8 + frames[i].Length;
        }

        return file;
    }

    public static JetstreamSegmentRow CommitRow(long seq, string did, string collection, string rkey, byte[]? payload) =>
        new()
        {
            Seq = seq,
            WitnessedAt = 1_000_000 + seq,
            Kind = payload == null ? JetstreamSegmentRowKind.Delete : JetstreamSegmentRowKind.Create,
            Did = did,
            Collection = collection,
            Rkey = rkey,
            Rev = "rev" + seq,
            Payload = payload,
        };
}

public class JetstreamSegmentFormatTests
{
    private static readonly byte[] SamplePayload = { 0xA1, 0x61, 0x61, 0x01 }; // {"a": 1} in CBOR

    [Fact]
    public void DecodeBlockFrame_RoundTripsAllColumns()
    {
        var rows = new List<JetstreamSegmentRow>
        {
            new()
            {
                Seq = 10,
                WitnessedAt = 111,
                IndexedAt = 222,
                Kind = JetstreamSegmentRowKind.Create,
                Did = "did:plc:alice",
                Collection = "app.bsky.feed.post",
                Rkey = "3k2a",
                Rev = "aaa",
                Payload = SamplePayload,
            },
            new()
            {
                Seq = 11,
                WitnessedAt = 333,
                Kind = JetstreamSegmentRowKind.Delete,
                Did = "did:plc:bob",
                Collection = "app.bsky.feed.like",
                Rkey = "3k2b",
                Rev = "bbb",
            },
            new()
            {
                Seq = 12,
                WitnessedAt = 444,
                Kind = JetstreamSegmentRowKind.Account,
                Did = "did:plc:carol",
                Payload = new byte[] { 0xA0 },
            },
        };

        var decoded = JetstreamSegmentFormat.DecodeBlockFrame(JetstreamSegmentTestData.EncodeBlockFrame(rows));

        Assert.Equal(3, decoded.Count);
        Assert.Equal(10, decoded[0].Seq);
        Assert.Equal(111, decoded[0].WitnessedAt);
        Assert.Equal(222, decoded[0].IndexedAt);
        Assert.Equal(222, decoded[0].DisplayTimeUs); // imported indexed_at wins
        Assert.Equal(JetstreamSegmentRowKind.Create, decoded[0].Kind);
        Assert.Equal("did:plc:alice", decoded[0].Did);
        Assert.Equal("app.bsky.feed.post", decoded[0].Collection);
        Assert.Equal("3k2a", decoded[0].Rkey);
        Assert.Equal("aaa", decoded[0].Rev);
        Assert.Equal(SamplePayload, decoded[0].Payload);

        Assert.Equal(JetstreamSegmentRowKind.Delete, decoded[1].Kind);
        Assert.Null(decoded[1].Payload);
        Assert.Equal(333, decoded[1].DisplayTimeUs); // no import → witnessed time

        Assert.Equal(JetstreamSegmentRowKind.Account, decoded[2].Kind);
        Assert.Equal(string.Empty, decoded[2].Collection);
    }

    [Fact]
    public void DecodeBlockBody_EmptyBlock_ReturnsNoRows()
    {
        var decoded = JetstreamSegmentFormat.DecodeBlockBody(new byte[] { 0, 0, 0, 0 });
        Assert.Empty(decoded);
    }

    [Fact]
    public void DecodeBlockBody_TruncatedBody_Throws()
    {
        var body = JetstreamSegmentTestData.EncodeBlockBody(new[]
        {
            JetstreamSegmentTestData.CommitRow(1, "did:plc:a", "c.o.l", "rk", SamplePayload),
        });
        var truncated = body.Take(body.Length - 2).ToArray();

        Assert.Throws<JetstreamV2Exception>(() => JetstreamSegmentFormat.DecodeBlockBody(truncated));
    }

    [Fact]
    public void DecodeBlockBody_TrailingBytes_Throws()
    {
        var body = JetstreamSegmentTestData.EncodeBlockBody(new[]
        {
            JetstreamSegmentTestData.CommitRow(1, "did:plc:a", "c.o.l", "rk", SamplePayload),
        });
        var padded = body.Concat(new byte[] { 0xFF }).ToArray();

        Assert.Throws<JetstreamV2Exception>(() => JetstreamSegmentFormat.DecodeBlockBody(padded));
    }

    [Fact]
    public void DecodeBlockBody_InvalidKind_Throws()
    {
        var body = JetstreamSegmentTestData.EncodeBlockBody(new[]
        {
            JetstreamSegmentTestData.CommitRow(1, "did:plc:a", "c.o.l", "rk", SamplePayload),
        });

        // The kind column starts after count (4) + seq/witnessed/indexed (3 × 8 per event).
        body[4 + 24] = 0;
        Assert.Throws<JetstreamV2Exception>(() => JetstreamSegmentFormat.DecodeBlockBody(body));
    }

    [Fact]
    public void DecodeBlockBody_HostileEventCount_Throws()
    {
        var body = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(body, uint.MaxValue);

        Assert.Throws<JetstreamV2Exception>(() => JetstreamSegmentFormat.DecodeBlockBody(body));
    }

    [Fact]
    public void ReadHeader_RejectsBadMagicActiveFileAndWrongVersion()
    {
        var file = JetstreamSegmentTestData.BuildSegmentFile(new[]
        {
            JetstreamSegmentTestData.CommitRow(1, "did:plc:a", "c.o.l", "rk", SamplePayload),
        });

        var badMagic = (byte[])file.Clone();
        badMagic[0] = (byte)'x';
        Assert.Throws<JetstreamV2Exception>(() => JetstreamSegmentFormat.ReadHeader(badMagic));

        var active = (byte[])file.Clone();
        active.AsSpan(4, 8).Clear(); // zero checksum = active file
        Assert.Throws<JetstreamV2Exception>(() => JetstreamSegmentFormat.ReadHeader(active));

        var badVersion = (byte[])file.Clone();
        badVersion[12] = 9;
        Assert.Throws<JetstreamV2Exception>(() => JetstreamSegmentFormat.ReadHeader(badVersion));

        Assert.Throws<JetstreamV2Exception>(() => JetstreamSegmentFormat.ReadHeader(new byte[10]));
    }

    [Fact]
    public void WholeSegmentFile_RoundTripsThroughHeaderAndBlockIndex()
    {
        var block0 = new[]
        {
            JetstreamSegmentTestData.CommitRow(1, "did:plc:a", "app.bsky.feed.post", "r1", SamplePayload),
            JetstreamSegmentTestData.CommitRow(2, "did:plc:a", "app.bsky.feed.post", "r2", null),
        };
        var block1 = new[]
        {
            JetstreamSegmentTestData.CommitRow(3, "did:plc:b", "app.bsky.feed.like", "r3", SamplePayload),
        };

        var file = JetstreamSegmentTestData.BuildSegmentFile(block0, block1);
        var header = JetstreamSegmentFormat.ReadHeader(file);

        Assert.Equal(2, header.BlockCount);
        Assert.Equal(3, header.EventCount);
        Assert.Equal(1, header.MinSeq);
        Assert.Equal(3, header.MaxSeq);

        var frame0 = JetstreamSegmentFormat.GetBlockFrame(file, header, 0);
        var rows0 = JetstreamSegmentFormat.DecodeBlockFrame(frame0);
        Assert.Equal(2, rows0.Count);
        Assert.Equal(1, rows0[0].Seq);
        Assert.Equal(2, rows0[1].Seq);

        var frame1 = JetstreamSegmentFormat.GetBlockFrame(file, header, 1);
        var rows1 = JetstreamSegmentFormat.DecodeBlockFrame(frame1);
        Assert.Single(rows1);
        Assert.Equal("did:plc:b", rows1[0].Did);

        Assert.Throws<JetstreamV2Exception>(() => JetstreamSegmentFormat.GetBlockFrame(file, header, 2));
        Assert.Throws<JetstreamV2Exception>(() => JetstreamSegmentFormat.GetBlockFrame(file, header, -1));
    }

    [Fact]
    public void GetBlockFrame_CorruptIndexEntry_Throws()
    {
        var file = JetstreamSegmentTestData.BuildSegmentFile(new[]
        {
            JetstreamSegmentTestData.CommitRow(1, "did:plc:a", "c.o.l", "rk", SamplePayload),
        });
        var header = JetstreamSegmentFormat.ReadHeader(file);

        // Point the block's offset past the footer: a hostile index entry must not drive an
        // out-of-bounds read.
        BinaryPrimitives.WriteUInt64LittleEndian(
            file.AsSpan((int)header.BlockIndexOffset, 8), (ulong)file.Length + 100);

        Assert.Throws<JetstreamV2Exception>(() => JetstreamSegmentFormat.GetBlockFrame(file, header, 0));
    }

    [Fact]
    public void DecodeBlockFrame_GarbageZstd_Throws()
    {
        Assert.Throws<JetstreamV2Exception>(() =>
            JetstreamSegmentFormat.DecodeBlockFrame(new byte[] { 1, 2, 3, 4, 5 }));
    }
}
