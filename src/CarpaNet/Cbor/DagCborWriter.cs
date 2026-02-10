using System;
using System.Formats.Cbor;
using System.Text;

namespace CarpaNet.Cbor;

/// <summary>
/// Wrapper around CborWriter with support for DAG-CBOR specific features like CID links (Tag 42).
/// </summary>
public ref struct DagCborWriter
{
    private CborWriter _writer;

    /// <summary>
    /// The CBOR tag for CID links in DAG-CBOR.
    /// </summary>
    public const CborTag CidTag = (CborTag)42;

    /// <summary>
    /// Creates a new DagCborWriter.
    /// </summary>
    public DagCborWriter()
    {
        _writer = new CborWriter(CborConformanceMode.Lax);
    }

    /// <summary>
    /// Creates a new DagCborWriter with the specified conformance mode.
    /// </summary>
    /// <param name="conformanceMode">The CBOR conformance mode.</param>
    public DagCborWriter(CborConformanceMode conformanceMode)
    {
        _writer = new CborWriter(conformanceMode);
    }

    /// <summary>
    /// Writes a signed 32-bit integer.
    /// </summary>
    public void WriteInt32(int value) => _writer.WriteInt32(value);

    /// <summary>
    /// Writes a signed 64-bit integer.
    /// </summary>
    public void WriteInt64(long value) => _writer.WriteInt64(value);

    /// <summary>
    /// Writes an unsigned 32-bit integer.
    /// </summary>
    public void WriteUInt32(uint value) => _writer.WriteUInt32(value);

    /// <summary>
    /// Writes an unsigned 64-bit integer.
    /// </summary>
    public void WriteUInt64(ulong value) => _writer.WriteUInt64(value);

    /// <summary>
    /// Writes a double-precision floating point value.
    /// </summary>
    public void WriteDouble(double value) => _writer.WriteDouble(value);

    /// <summary>
    /// Writes a single-precision floating point value.
    /// </summary>
    public void WriteSingle(float value) => _writer.WriteSingle(value);

    /// <summary>
    /// Writes a boolean value.
    /// </summary>
    public void WriteBoolean(bool value) => _writer.WriteBoolean(value);

    /// <summary>
    /// Writes a null value.
    /// </summary>
    public void WriteNull() => _writer.WriteNull();

    /// <summary>
    /// Writes a text string.
    /// </summary>
    public void WriteTextString(string value) => _writer.WriteTextString(value);

    /// <summary>
    /// Writes a byte string.
    /// </summary>
    public void WriteByteString(ReadOnlySpan<byte> value) => _writer.WriteByteString(value);

    /// <summary>
    /// Writes a byte string.
    /// </summary>
    public void WriteByteString(byte[] value) => _writer.WriteByteString(value);

    /// <summary>
    /// Writes the start of a map with the specified number of items.
    /// </summary>
    public void WriteStartMap(int? itemCount) => _writer.WriteStartMap(itemCount);

    /// <summary>
    /// Writes the end of a map.
    /// </summary>
    public void WriteEndMap() => _writer.WriteEndMap();

    /// <summary>
    /// Writes the start of an array with the specified number of items.
    /// </summary>
    public void WriteStartArray(int? itemCount) => _writer.WriteStartArray(itemCount);

    /// <summary>
    /// Writes the end of an array.
    /// </summary>
    public void WriteEndArray() => _writer.WriteEndArray();

    /// <summary>
    /// Writes a CBOR tag.
    /// </summary>
    public void WriteTag(CborTag tag) => _writer.WriteTag(tag);

    /// <summary>
    /// Writes a CID link (Tag 42 + byte string with multibase prefix).
    /// </summary>
    /// <param name="cid">The CID to write.</param>
    public void WriteCidLink(ATCid cid)
    {
        if (cid.Value == null || cid.Value.Length == 0)
        {
            WriteNull();
            return;
        }

        _writer.WriteTag(CidTag);

        // Decode the CID string to bytes
        var cidBytes = DecodeCidFromString(cid.Value);

        // Add multibase prefix (0x00) for binary encoding
        var bytesWithPrefix = new byte[cidBytes.Length + 1];
        bytesWithPrefix[0] = 0x00;
        cidBytes.CopyTo(bytesWithPrefix, 1);

        _writer.WriteByteString(bytesWithPrefix);
    }

    /// <summary>
    /// Encodes the written CBOR data to a byte array.
    /// </summary>
    public byte[] Encode() => _writer.Encode();

    /// <summary>
    /// Gets the number of bytes written.
    /// </summary>
    public int BytesWritten => _writer.BytesWritten;

    /// <summary>
    /// Decodes a CID string (base32 with 'b' prefix) to bytes.
    /// </summary>
    private static byte[] DecodeCidFromString(string cidString)
    {
        if (string.IsNullOrEmpty(cidString))
        {
            return Array.Empty<byte>();
        }

        // CIDv1 strings start with 'b' for base32lower encoding
        if (cidString.StartsWith("b", StringComparison.Ordinal))
        {
            return Base32Lower.Decode(cidString.Substring(1));
        }

        // CIDv0 (base58btc, starts with 'Qm')
        if (cidString.StartsWith("Qm", StringComparison.Ordinal))
        {
            return Base58.Decode(cidString);
        }

        // Try to decode as base32 without prefix
        return Base32Lower.Decode(cidString);
    }

    /// <summary>
    /// RFC 4648 base32 decoding without padding, lowercase.
    /// </summary>
    private static class Base32Lower
    {
        private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

        public static byte[] Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded))
            {
                return Array.Empty<byte>();
            }

            // Remove padding if present
            encoded = encoded.TrimEnd('=');

            var outputLength = encoded.Length * 5 / 8;
            var result = new byte[outputLength];

            var buffer = 0;
            var bitsLeft = 0;
            var index = 0;

            foreach (var c in encoded)
            {
                var charIndex = Alphabet.IndexOf(char.ToLowerInvariant(c));
                if (charIndex < 0)
                {
                    throw new FormatException($"Invalid base32 character: {c}");
                }

                buffer = (buffer << 5) | charIndex;
                bitsLeft += 5;

                if (bitsLeft >= 8)
                {
                    bitsLeft -= 8;
                    result[index++] = (byte)(buffer >> bitsLeft);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Base58 Bitcoin encoding (for CIDv0 compatibility).
    /// </summary>
    private static class Base58
    {
        private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        public static byte[] Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded))
            {
                return Array.Empty<byte>();
            }

            // Count leading zeros
            var leadingZeros = 0;
            foreach (var c in encoded)
            {
                if (c == '1')
                {
                    leadingZeros++;
                }
                else
                {
                    break;
                }
            }

            // Decode
            var size = encoded.Length * 733 / 1000 + 1; // log(58) / log(256)
            var bytes = new byte[size];
            var length = 0;

            foreach (var c in encoded)
            {
                var value = Alphabet.IndexOf(c);
                if (value < 0)
                {
                    throw new FormatException($"Invalid base58 character: {c}");
                }

                var carry = value;
                for (var i = 0; i < length; i++)
                {
                    carry += 58 * bytes[i];
                    bytes[i] = (byte)(carry & 0xFF);
                    carry >>= 8;
                }

                while (carry > 0)
                {
                    bytes[length++] = (byte)(carry & 0xFF);
                    carry >>= 8;
                }
            }

            // Reverse and add leading zeros
            var result = new byte[leadingZeros + length];
            for (var i = 0; i < length; i++)
            {
                result[leadingZeros + i] = bytes[length - 1 - i];
            }

            return result;
        }
    }
}
