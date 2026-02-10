using System;
using System.Formats.Cbor;
using System.Text;

namespace CarpaNet.Cbor;

/// <summary>
/// Wrapper around CborReader with support for DAG-CBOR specific features like CID links (Tag 42).
/// </summary>
public ref struct DagCborReader
{
    private CborReader _reader;
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _initialLength;

    /// <summary>
    /// The CBOR tag for CID links in DAG-CBOR.
    /// </summary>
    public const CborTag CidTag = (CborTag)42;

    /// <summary>
    /// Creates a new DagCborReader from byte data.
    /// </summary>
    /// <param name="data">The CBOR-encoded data.</param>
    /// <param name="allowMultipleRootLevelValues">Whether to allow multiple root-level values (needed for frame parsing).</param>
    public DagCborReader(ReadOnlyMemory<byte> data, bool allowMultipleRootLevelValues = false)
    {
        _data = data;
        _initialLength = data.Length;
        _reader = new CborReader(data, CborConformanceMode.Lax, allowMultipleRootLevelValues);
    }

    /// <summary>
    /// Gets the number of bytes that have been read from the input.
    /// </summary>
    public int BytesRead => _initialLength - _reader.BytesRemaining;

    /// <summary>
    /// Gets the number of bytes remaining in the input.
    /// </summary>
    public int BytesRemaining => _reader.BytesRemaining;

    /// <summary>
    /// Gets the remaining unread data as a ReadOnlyMemory.
    /// Used for union deserialization where we need to peek then re-read.
    /// </summary>
    /// <returns>The remaining unread bytes.</returns>
    public ReadOnlyMemory<byte> GetRemainingData()
    {
        return _data.Slice(BytesRead);
    }

    /// <summary>
    /// Peeks the next CBOR state without advancing the reader.
    /// </summary>
    public CborReaderState PeekState() => _reader.PeekState();

    /// <summary>
    /// Reads a signed 32-bit integer.
    /// </summary>
    public int ReadInt32() => _reader.ReadInt32();

    /// <summary>
    /// Reads a signed 64-bit integer.
    /// </summary>
    public long ReadInt64() => _reader.ReadInt64();

    /// <summary>
    /// Reads an unsigned 32-bit integer.
    /// </summary>
    public uint ReadUInt32() => _reader.ReadUInt32();

    /// <summary>
    /// Reads an unsigned 64-bit integer.
    /// </summary>
    public ulong ReadUInt64() => _reader.ReadUInt64();

    /// <summary>
    /// Reads a double-precision floating point value.
    /// </summary>
    public double ReadDouble() => _reader.ReadDouble();

    /// <summary>
    /// Reads a single-precision floating point value.
    /// </summary>
    public float ReadSingle() => _reader.ReadSingle();

    /// <summary>
    /// Reads a boolean value.
    /// </summary>
    public bool ReadBoolean() => _reader.ReadBoolean();

    /// <summary>
    /// Reads a null value.
    /// </summary>
    public void ReadNull() => _reader.ReadNull();

    /// <summary>
    /// Reads a text string.
    /// </summary>
    public string ReadTextString() => _reader.ReadTextString();

    /// <summary>
    /// Reads a byte string.
    /// </summary>
    public byte[] ReadByteString() => _reader.ReadByteString();

    /// <summary>
    /// Reads the start of a map and returns the number of items (or null if indefinite length).
    /// </summary>
    public int? ReadStartMap() => _reader.ReadStartMap();

    /// <summary>
    /// Reads the end of a map.
    /// </summary>
    public void ReadEndMap() => _reader.ReadEndMap();

    /// <summary>
    /// Reads the start of an array and returns the number of items (or null if indefinite length).
    /// </summary>
    public int? ReadStartArray() => _reader.ReadStartArray();

    /// <summary>
    /// Reads the end of an array.
    /// </summary>
    public void ReadEndArray() => _reader.ReadEndArray();

    /// <summary>
    /// Reads a CBOR tag.
    /// </summary>
    public CborTag ReadTag() => _reader.ReadTag();

    /// <summary>
    /// Skips the next CBOR value (useful for unknown fields).
    /// </summary>
    public void SkipValue() => _reader.SkipValue();

    /// <summary>
    /// Reads a CID link (Tag 42 + byte string).
    /// </summary>
    /// <returns>The CID as an ATCid.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the next value is not a CID link.</exception>
    public ATCid ReadCidLink()
    {
        var tag = _reader.ReadTag();
        if (tag != CidTag)
        {
            throw new InvalidOperationException($"Expected CID tag (42), got {(int)tag}");
        }

        var cidBytes = _reader.ReadByteString();

        // CID bytes in DAG-CBOR have a 0x00 multibase prefix which we skip
        // Then we encode the remaining bytes as a CID string
        if (cidBytes.Length == 0)
        {
            return new ATCid(string.Empty);
        }

        // Skip the multibase prefix (0x00) if present
        var offset = cidBytes[0] == 0x00 ? 1 : 0;
        var cidData = new byte[cidBytes.Length - offset];
        Array.Copy(cidBytes, offset, cidData, 0, cidData.Length);

        // Encode the CID bytes to a string representation
        var cidString = EncodeCidToString(cidData);
        return new ATCid(cidString);
    }

    /// <summary>
    /// Checks if the next value is a CID link (Tag 42).
    /// </summary>
    public bool IsCidLink()
    {
        if (PeekState() != CborReaderState.Tag)
        {
            return false;
        }

        // We need to peek the tag value, but CborReader doesn't have a PeekTag method
        // So we'll check the state and let the caller handle it
        return true; // Caller should try ReadCidLink and handle exceptions
    }

    /// <summary>
    /// Tries to read a CID link if the next value is one.
    /// </summary>
    /// <param name="cid">The CID if successful.</param>
    /// <returns>True if a CID was read, false otherwise.</returns>
    public bool TryReadCidLink(out ATCid cid)
    {
        if (PeekState() != CborReaderState.Tag)
        {
            cid = default;
            return false;
        }

        try
        {
            cid = ReadCidLink();
            return true;
        }
        catch
        {
            cid = default;
            return false;
        }
    }

    /// <summary>
    /// Encodes CID bytes to a base32 string (CIDv1 format).
    /// </summary>
    private static string EncodeCidToString(byte[] cidData)
    {
        if (cidData.Length == 0)
        {
            return string.Empty;
        }

        // CIDv1 format: multicodec + multihash
        // For ATProtocol, CIDs are typically CIDv1 with dag-cbor codec (0x71) and SHA-256 hash
        // The string representation uses base32lower encoding prefixed with 'b'

        // Use base32 encoding (RFC 4648 without padding, lowercase)
        return "b" + Base32Lower.Encode(cidData);
    }

    /// <summary>
    /// RFC 4648 base32 encoding without padding, lowercase.
    /// </summary>
    private static class Base32Lower
    {
        private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

        public static string Encode(byte[] data)
        {
            if (data.Length == 0)
            {
                return string.Empty;
            }

            var outputLength = (data.Length * 8 + 4) / 5;
            var result = new StringBuilder(outputLength);

            int buffer = 0;
            int bitsLeft = 0;

            foreach (var b in data)
            {
                buffer = (buffer << 8) | b;
                bitsLeft += 8;

                while (bitsLeft >= 5)
                {
                    bitsLeft -= 5;
                    var index = (buffer >> bitsLeft) & 0x1F;
                    result.Append(Alphabet[index]);
                }
            }

            if (bitsLeft > 0)
            {
                var index = (buffer << (5 - bitsLeft)) & 0x1F;
                result.Append(Alphabet[index]);
            }

            return result.ToString();
        }
    }
}
