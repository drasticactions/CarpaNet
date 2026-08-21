using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using ZstdSharp;

namespace CarpaNet.Jetstream;

/// <summary>
/// Reads the sealed Jetstream v2 segment file format: the fixed header, the stored
/// per-block zstd frames, and the columnar block body.
/// </summary>
public static class JetstreamSegmentFormat
{
    /// <summary>Size in bytes of the fixed segment header.</summary>
    public const int HeaderSize = 256;

    private const ushort CurrentHeaderVersion = 1;
    private const int BlockIndexEntrySize = 52;

    private const int MaxBlockEvents = 1 << 18;

    private const long MaxDecodedBlockBytes = 1L << 30;

    private static readonly byte[] SegmentMagic = { (byte)'j', (byte)'s', (byte)'s', (byte)'0' };

    /// <summary>
    /// Parses and validates the fixed header of a sealed segment file. The input may be the
    /// whole file or just its first <see cref="HeaderSize"/> bytes.
    /// </summary>
    /// <param name="segmentBytes">The sealed segment file bytes.</param>
    /// <returns>The parsed header.</returns>
    /// <exception cref="JetstreamV2Exception">The header is truncated, unsealed, or corrupt.</exception>
    public static JetstreamSegmentHeader ReadHeader(ReadOnlySpan<byte> segmentBytes)
    {
        if (segmentBytes.Length < HeaderSize)
        {
            throw new JetstreamV2Exception($"segment header is {segmentBytes.Length} bytes, want at least {HeaderSize}");
        }

        if (!segmentBytes.Slice(0, 4).SequenceEqual(SegmentMagic))
        {
            throw new JetstreamV2Exception("segment has bad magic; not a sealed Jetstream segment file");
        }

        var checksum = BinaryPrimitives.ReadUInt64LittleEndian(segmentBytes.Slice(4, 8));
        if (checksum == 0)
        {
            throw new JetstreamV2Exception("segment checksum field is zero; this is an active (unsealed) segment");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(segmentBytes.Slice(12, 2));
        if (version != CurrentHeaderVersion)
        {
            throw new JetstreamV2Exception($"segment header version {version}, want {CurrentHeaderVersion}");
        }

        var blockCount = BinaryPrimitives.ReadUInt32LittleEndian(segmentBytes.Slice(14, 4));
        if (blockCount > int.MaxValue)
        {
            throw new JetstreamV2Exception($"segment block count {blockCount} is out of range");
        }

        var footerOffset = BinaryPrimitives.ReadUInt64LittleEndian(segmentBytes.Slice(58, 8));
        var blockIndexOffset = BinaryPrimitives.ReadUInt64LittleEndian(segmentBytes.Slice(90, 8));
        if (footerOffset > long.MaxValue || blockIndexOffset > long.MaxValue)
        {
            throw new JetstreamV2Exception("segment header offsets are out of range");
        }

        return new JetstreamSegmentHeader
        {
            Version = version,
            BlockCount = (int)blockCount,
            EventCount = BinaryPrimitives.ReadUInt32LittleEndian(segmentBytes.Slice(18, 4)),
            MinSeq = ReadSeq(segmentBytes.Slice(26, 8), "minSeq"),
            MaxSeq = ReadSeq(segmentBytes.Slice(34, 8), "maxSeq"),
            MinWitnessedAt = (long)BinaryPrimitives.ReadUInt64LittleEndian(segmentBytes.Slice(42, 8)),
            MaxWitnessedAt = (long)BinaryPrimitives.ReadUInt64LittleEndian(segmentBytes.Slice(50, 8)),
            FooterOffset = (long)footerOffset,
            BlockIndexOffset = (long)blockIndexOffset,
        };
    }

    /// <summary>
    /// Extracts the raw, stored zstd frame for one block from a whole downloaded segment file,
    /// using the block index the header points at.
    /// </summary>
    /// <param name="segmentBytes">The whole sealed segment file.</param>
    /// <param name="header">The header parsed from the same bytes.</param>
    /// <param name="index">The zero-based block index.</param>
    /// <returns>The block's compressed frame bytes.</returns>
    /// <exception cref="JetstreamV2Exception">The index is out of range or the block index is corrupt.</exception>
    public static byte[] GetBlockFrame(byte[] segmentBytes, JetstreamSegmentHeader header, int index)
    {
        if (segmentBytes == null)
        {
            throw new ArgumentNullException(nameof(segmentBytes));
        }

        if (header == null)
        {
            throw new ArgumentNullException(nameof(header));
        }

        if (index < 0 || index >= header.BlockCount)
        {
            throw new JetstreamV2Exception($"block index {index} out of range; segment has {header.BlockCount} blocks");
        }

        if (header.FooterOffset < HeaderSize || header.FooterOffset > segmentBytes.Length)
        {
            throw new JetstreamV2Exception($"segment footer offset {header.FooterOffset} is out of range");
        }

        if (header.BlockIndexOffset != header.FooterOffset)
        {
            throw new JetstreamV2Exception(
                $"segment block index offset {header.BlockIndexOffset} does not match footer offset {header.FooterOffset}");
        }

        var entryOffset = header.BlockIndexOffset + (long)index * BlockIndexEntrySize;
        if (entryOffset + BlockIndexEntrySize > segmentBytes.Length)
        {
            throw new JetstreamV2Exception($"segment block index entry {index} lies past the end of the file");
        }

        var entry = segmentBytes.AsSpan((int)entryOffset, BlockIndexEntrySize);
        var offset = BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(0, 8));
        var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8, 4));

        // Validate the frame range lies within [HeaderSize, FooterOffset). The stored frame is
        // preceded by an 8-byte length prefix that is not part of the frame itself.
        var footer = (ulong)header.FooterOffset;
        if (compressedSize > footer ||
            offset > footer - 8 ||
            compressedSize > footer - offset - 8 ||
            offset < HeaderSize)
        {
            throw new JetstreamV2Exception($"segment block {index} range is outside the data region");
        }

        var frame = new byte[compressedSize];
        Array.Copy(segmentBytes, (long)offset + 8, frame, 0, compressedSize);
        return frame;
    }

    /// <summary>
    /// Decompresses and decodes a single raw block frame into its rows.
    /// </summary>
    /// <param name="frame">The compressed block frame.</param>
    /// <returns>The decoded rows, in stored (per-DID) order.</returns>
    /// <exception cref="JetstreamV2Exception">The frame is corrupt or would decompress past the safety cap.</exception>
    public static IReadOnlyList<JetstreamSegmentRow> DecodeBlockFrame(byte[] frame)
    {
        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        byte[] body;
        try
        {
            using var decompressor = new Decompressor();
            var contentSize = Decompressor.GetDecompressedSize(frame);
            if (contentSize > MaxDecodedBlockBytes)
            {
                throw new JetstreamV2Exception(
                    $"segment block would decompress to {contentSize} bytes, over the {MaxDecodedBlockBytes} byte cap");
            }

            body = decompressor.Unwrap(frame).ToArray();
        }
        catch (JetstreamV2Exception)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JetstreamV2Exception($"segment block zstd decompress failed: {ex.Message}", ex);
        }

        return DecodeBlockBody(body);
    }

    /// <summary>
    /// Decodes an uncompressed columnar block body. Exposed for tooling; most callers use
    /// <see cref="DecodeBlockFrame(byte[])"/>.
    /// </summary>
    /// <param name="body">The uncompressed block body.</param>
    /// <returns>The decoded rows.</returns>
    /// <exception cref="JetstreamV2Exception">The body is truncated or malformed.</exception>
    public static IReadOnlyList<JetstreamSegmentRow> DecodeBlockBody(byte[] body)
    {
        if (body == null)
        {
            throw new ArgumentNullException(nameof(body));
        }

        const int FixedPerEvent = 8 + 8 + 8 + 1 + 1 + 2 + 1 + 1 + 4;

        if (body.Length < 4)
        {
            throw Truncated();
        }

        var span = body.AsSpan();
        long eventCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0, 4));
        var offset = 4;

        if (eventCount > MaxBlockEvents)
        {
            throw Truncated();
        }

        if (body.Length - offset < eventCount * FixedPerEvent)
        {
            throw Truncated();
        }

        var count = (int)eventCount;
        if (count == 0)
        {
            if (offset != body.Length)
            {
                throw Truncated();
            }

            return Array.Empty<JetstreamSegmentRow>();
        }

        var rows = new JetstreamSegmentRow[count];
        for (var i = 0; i < count; i++)
        {
            rows[i] = new JetstreamSegmentRow();
        }

        // Fixed-size columns, in spec order.
        for (var i = 0; i < count; i++)
        {
            rows[i].Seq = ReadSeq(span.Slice(offset + i * 8, 8), "row seq");
        }

        offset += count * 8;
        for (var i = 0; i < count; i++)
        {
            rows[i].WitnessedAt = (long)BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset + i * 8, 8));
        }

        offset += count * 8;
        for (var i = 0; i < count; i++)
        {
            rows[i].IndexedAt = (long)BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset + i * 8, 8));
        }

        offset += count * 8;
        for (var i = 0; i < count; i++)
        {
            var kind = body[offset + i];
            if (kind < (byte)JetstreamSegmentRowKind.Create || kind > (byte)JetstreamSegmentRowKind.CreateResync)
            {
                throw Truncated();
            }

            rows[i].Kind = (JetstreamSegmentRowKind)kind;
        }

        offset += count;
        var collLenOffset = offset;
        offset += count;
        var didLenOffset = offset;
        offset += count * 2;
        var rkeyLenOffset = offset;
        offset += count;
        var revLenOffset = offset;
        offset += count;
        var payloadLenOffset = offset;
        offset += count * 4;

        // Sum the variable-length blobs, trapping overflow before any allocation.
        long totalColl = 0, totalDid = 0, totalRkey = 0, totalRev = 0, totalPayload = 0;
        for (var i = 0; i < count; i++)
        {
            totalColl += body[collLenOffset + i];
            totalDid += BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(didLenOffset + i * 2, 2));
            totalRkey += body[rkeyLenOffset + i];
            totalRev += body[revLenOffset + i];
            totalPayload += BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(payloadLenOffset + i * 4, 4));
        }

        var remaining = (long)body.Length - offset;
        if (totalColl + totalDid + totalRkey + totalRev + totalPayload != remaining)
        {
            // encodeBlock produces an exact-length buffer; anything else is corruption.
            throw Truncated();
        }

        var collOffset = offset;
        var didOffset = collOffset + (int)totalColl;
        var rkeyOffset = didOffset + (int)totalDid;
        var revOffset = rkeyOffset + (int)totalRkey;
        var payloadOffset = revOffset + (int)totalRev;

        for (var i = 0; i < count; i++)
        {
            var collLen = body[collLenOffset + i];
            var didLen = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(didLenOffset + i * 2, 2));
            var rkeyLen = body[rkeyLenOffset + i];
            var revLen = body[revLenOffset + i];
            var payloadLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(payloadLenOffset + i * 4, 4));

            var row = rows[i];
            row.Collection = collLen == 0 ? string.Empty : Encoding.UTF8.GetString(body, collOffset, collLen);
            row.Did = didLen == 0 ? string.Empty : Encoding.UTF8.GetString(body, didOffset, didLen);
            row.Rkey = rkeyLen == 0 ? string.Empty : Encoding.UTF8.GetString(body, rkeyOffset, rkeyLen);
            row.Rev = revLen == 0 ? string.Empty : Encoding.UTF8.GetString(body, revOffset, revLen);
            if (payloadLen > 0)
            {
                var payload = new byte[payloadLen];
                Array.Copy(body, payloadOffset, payload, 0, payloadLen);
                row.Payload = payload;
            }

            collOffset += collLen;
            didOffset += didLen;
            rkeyOffset += rkeyLen;
            revOffset += revLen;
            payloadOffset += payloadLen;
        }

        return rows;
    }

    private static long ReadSeq(ReadOnlySpan<byte> span, string field)
    {
        var value = BinaryPrimitives.ReadUInt64LittleEndian(span);
        if (value > long.MaxValue)
        {
            throw new JetstreamV2Exception($"segment {field} {value} is out of range");
        }

        return (long)value;
    }

    private static JetstreamV2Exception Truncated() =>
        new("truncated or malformed segment block");
}
