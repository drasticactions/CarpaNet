using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarpaNet;

/// <summary>
/// Represents a Content Identifier (CID) which is a hash-based link to content.
/// In JSON, CIDs are represented as { "$link": "bafyrei..." }.
///
/// ATProtocol uses CIDv1 with the following "blessed" format:
/// - Multibase: binary (0x00) for DAG-CBOR, base32lower ('b') for JSON/strings
/// - Multicodec: dag-cbor (0x71)
/// - Multihash: sha-256 (0x12) with 256 bits (32 bytes)
/// </summary>
[JsonConverter(typeof(ATCidJsonConverter))]
public readonly struct ATCid : IEquatable<ATCid>
{
    // CID version constants
    private const int CidV0 = 0;
    private const int CidV1 = 1;

    // Multicodec constants
    /// <summary>dag-cbor multicodec (0x71)</summary>
    public const int MulticodecDagCbor = 0x71;
    /// <summary>dag-pb multicodec (0x70) - used in CIDv0</summary>
    public const int MulticodecDagPb = 0x70;
    /// <summary>raw multicodec (0x55)</summary>
    public const int MulticodecRaw = 0x55;

    // Multihash constants
    /// <summary>SHA-256 multihash type (0x12)</summary>
    public const int MultihashSha256 = 0x12;
    /// <summary>SHA-256 hash length in bytes (32)</summary>
    public const int Sha256HashLength = 32;

    /// <summary>
    /// The raw CID string value (base32lower encoded for CIDv1).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The CID version (0 or 1). Returns -1 if the CID is invalid.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// The multicodec value (e.g., 0x71 for dag-cbor). Returns -1 if the CID is invalid.
    /// </summary>
    public int Multicodec { get; }

    /// <summary>
    /// The multihash type (e.g., 0x12 for sha-256). Returns -1 if the CID is invalid.
    /// </summary>
    public int MultihashType { get; }

    /// <summary>
    /// The hash length in bytes. Returns -1 if the CID is invalid.
    /// </summary>
    public int HashLength { get; }

    /// <summary>
    /// The raw hash bytes. Returns null if the CID is invalid.
    /// </summary>
    public byte[]? Hash { get; }

    /// <summary>
    /// Creates a new ATCid from a string value.
    /// </summary>
    public ATCid(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));

        if (string.IsNullOrEmpty(value))
        {
            Version = -1;
            Multicodec = -1;
            MultihashType = -1;
            HashLength = -1;
            Hash = null;
            return;
        }

        // Try to parse the CID
        if (!TryParseCid(value, out var version, out var multicodec, out var multihashType, out var hashLength, out var hash))
        {
            Version = -1;
            Multicodec = -1;
            MultihashType = -1;
            HashLength = -1;
            Hash = null;
            return;
        }

        Version = version;
        Multicodec = multicodec;
        MultihashType = multihashType;
        HashLength = hashLength;
        Hash = hash;
    }

    /// <summary>
    /// Private constructor for creating ATCid with pre-parsed values.
    /// </summary>
    private ATCid(string value, int version, int multicodec, int multihashType, int hashLength, byte[]? hash)
    {
        Value = value;
        Version = version;
        Multicodec = multicodec;
        MultihashType = multihashType;
        HashLength = hashLength;
        Hash = hash;
    }

    /// <summary>
    /// Returns true if this CID was successfully parsed.
    /// </summary>
    public bool IsValid => Version >= 0 && Hash != null;

    /// <summary>
    /// Returns true if this CID follows the ATProtocol "blessed" format:
    /// CIDv1, dag-cbor (0x71), sha-256 (0x12), 32 bytes.
    /// </summary>
    public bool IsAtProtoBlessedFormat =>
        Version == CidV1 &&
        Multicodec == MulticodecDagCbor &&
        MultihashType == MultihashSha256 &&
        HashLength == Sha256HashLength;

    /// <summary>
    /// Returns true if this is a CIDv0 (base58btc, starts with "Qm").
    /// </summary>
    public bool IsCidV0 => Version == CidV0;

    /// <summary>
    /// Returns true if this is a CIDv1.
    /// </summary>
    public bool IsCidV1 => Version == CidV1;

    /// <summary>
    /// Creates a CIDv1 from raw SHA-256 hash bytes using the ATProtocol blessed format.
    /// </summary>
    /// <param name="sha256Hash">The 32-byte SHA-256 hash.</param>
    /// <returns>A new ATCid in the blessed format.</returns>
    /// <exception cref="ArgumentException">Thrown if the hash is not 32 bytes.</exception>
    public static ATCid FromSha256Hash(byte[] sha256Hash)
    {
        if (sha256Hash == null)
            throw new ArgumentNullException(nameof(sha256Hash));
        if (sha256Hash.Length != Sha256HashLength)
            throw new ArgumentException($"SHA-256 hash must be {Sha256HashLength} bytes, got {sha256Hash.Length}", nameof(sha256Hash));

        // Build CIDv1: version (1) + multicodec (dag-cbor) + multihash (sha256 type + length + hash)
        var cidBytes = new byte[1 + 1 + 1 + 1 + Sha256HashLength]; // version + multicodec + hash type + hash length + hash
        var offset = 0;

        // Version 1
        cidBytes[offset++] = CidV1;
        // Multicodec: dag-cbor (0x71)
        cidBytes[offset++] = MulticodecDagCbor;
        // Multihash type: sha-256 (0x12)
        cidBytes[offset++] = MultihashSha256;
        // Hash length: 32 (0x20)
        cidBytes[offset++] = Sha256HashLength;
        // Hash bytes
        Array.Copy(sha256Hash, 0, cidBytes, offset, Sha256HashLength);

        // Encode as base32lower with 'b' prefix
        var value = "b" + Base32Lower.Encode(cidBytes);

        // Clone the hash to ensure immutability
        var hashCopy = new byte[Sha256HashLength];
        Array.Copy(sha256Hash, hashCopy, Sha256HashLength);

        return new ATCid(value, CidV1, MulticodecDagCbor, MultihashSha256, Sha256HashLength, hashCopy);
    }

    /// <summary>
    /// Creates an ATCid from raw CID bytes (binary representation without multibase prefix).
    /// </summary>
    /// <param name="cidBytes">The raw CID bytes.</param>
    /// <returns>A new ATCid.</returns>
    public static ATCid FromBytes(byte[] cidBytes)
    {
        if (cidBytes == null)
            throw new ArgumentNullException(nameof(cidBytes));
        if (cidBytes.Length == 0)
            return new ATCid(string.Empty);

        // Encode as base32lower with 'b' prefix
        var value = "b" + Base32Lower.Encode(cidBytes);
        return new ATCid(value);
    }

    /// <summary>
    /// Gets the raw CID bytes (binary representation without multibase prefix).
    /// </summary>
    /// <returns>The raw CID bytes, or an empty array if invalid.</returns>
    public byte[] ToBytes()
    {
        if (string.IsNullOrEmpty(Value))
            return Array.Empty<byte>();

        // CIDv1 base32lower (starts with 'b')
        if (Value.StartsWith("b", StringComparison.Ordinal) && Value.Length > 1)
        {
            return Base32Lower.Decode(Value.Substring(1));
        }

        // CIDv0 base58btc (starts with 'Qm')
        if (Value.StartsWith("Qm", StringComparison.Ordinal))
        {
            return Base58Btc.Decode(Value);
        }

        // Try base32 without prefix as fallback
        return Base32Lower.Decode(Value);
    }

    /// <summary>
    /// Tries to parse a CID string and extract its components.
    /// </summary>
    private static bool TryParseCid(string value, out int version, out int multicodec, out int multihashType, out int hashLength, out byte[]? hash)
    {
        version = -1;
        multicodec = -1;
        multihashType = -1;
        hashLength = -1;
        hash = null;

        if (string.IsNullOrEmpty(value))
            return false;

        byte[] cidBytes;

        // CIDv0: base58btc encoded, starts with "Qm" (SHA-256 multihash)
        if (value.StartsWith("Qm", StringComparison.Ordinal))
        {
            try
            {
                cidBytes = Base58Btc.Decode(value);
            }
            catch
            {
                return false;
            }

            // CIDv0 is just a multihash (no version or multicodec prefix)
            // Format: hash type (1 byte) + hash length (1 byte) + hash
            if (cidBytes.Length < 2)
                return false;

            version = CidV0;
            multicodec = MulticodecDagPb; // CIDv0 implicitly uses dag-pb
            multihashType = cidBytes[0];
            hashLength = cidBytes[1];

            if (cidBytes.Length < 2 + hashLength)
                return false;

            hash = new byte[hashLength];
            Array.Copy(cidBytes, 2, hash, 0, hashLength);
            return true;
        }

        // CIDv1: base32lower encoded with 'b' prefix (most common in ATProtocol)
        if (value.StartsWith("b", StringComparison.Ordinal) && value.Length > 1)
        {
            try
            {
                cidBytes = Base32Lower.Decode(value.Substring(1));
            }
            catch
            {
                return false;
            }
        }
        // CIDv1 with other multibase prefixes
        else if (value.StartsWith("z", StringComparison.Ordinal)) // base58btc
        {
            try
            {
                cidBytes = Base58Btc.Decode(value.Substring(1));
            }
            catch
            {
                return false;
            }
        }
        else if (value.StartsWith("f", StringComparison.Ordinal)) // base16lower
        {
            try
            {
                cidBytes = HexDecode(value.Substring(1));
            }
            catch
            {
                return false;
            }
        }
        else
        {
            // Try base32 without prefix as fallback
            try
            {
                cidBytes = Base32Lower.Decode(value);
            }
            catch
            {
                return false;
            }
        }

        // Parse CIDv1: version + multicodec (varints) + multihash
        if (cidBytes.Length < 4) // minimum: version + multicodec + hash type + hash length
            return false;

        var offset = 0;

        // Read version (varint)
        if (!TryReadVarint(cidBytes, ref offset, out var v))
            return false;
        version = (int)v;

        if (version != CidV1)
            return false; // Not CIDv1

        // Read multicodec (varint)
        if (!TryReadVarint(cidBytes, ref offset, out var mc))
            return false;
        multicodec = (int)mc;

        // Read multihash type (varint)
        if (!TryReadVarint(cidBytes, ref offset, out var mht))
            return false;
        multihashType = (int)mht;

        // Read hash length (varint)
        if (!TryReadVarint(cidBytes, ref offset, out var hl))
            return false;
        hashLength = (int)hl;

        // Validate and read hash
        if (offset + hashLength > cidBytes.Length)
            return false;

        hash = new byte[hashLength];
        Array.Copy(cidBytes, offset, hash, 0, hashLength);

        return true;
    }

    /// <summary>
    /// Reads an unsigned varint from a byte array.
    /// </summary>
    private static bool TryReadVarint(byte[] data, ref int offset, out ulong value)
    {
        value = 0;
        var shift = 0;

        while (offset < data.Length)
        {
            var b = data[offset++];
            value |= (ulong)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
                return true;

            shift += 7;
            if (shift > 63)
                return false; // Overflow
        }

        return false; // Incomplete varint
    }

    public bool Equals(ATCid other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ATCid other && Equals(other);
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;
    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(ATCid left, ATCid right) => left.Equals(right);
    public static bool operator !=(ATCid left, ATCid right) => !left.Equals(right);

    public static implicit operator string(ATCid cid) => cid.Value;
    public static implicit operator ATCid(string value) => new(value);

    /// <summary>
    /// RFC 4648 base32 encoding/decoding without padding, lowercase.
    /// </summary>
    private static class Base32Lower
    {
        private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

        public static string Encode(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

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

        public static byte[] Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded))
                return Array.Empty<byte>();

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
                    throw new FormatException($"Invalid base32 character: {c}");

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
    /// Base58 Bitcoin encoding/decoding.
    /// </summary>
    private static class Base58Btc
    {
        private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        public static byte[] Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded))
                return Array.Empty<byte>();

            // Count leading zeros
            var leadingZeros = 0;
            foreach (var c in encoded)
            {
                if (c == '1')
                    leadingZeros++;
                else
                    break;
            }

            // Decode
            var size = encoded.Length * 733 / 1000 + 1; // log(58) / log(256)
            var bytes = new byte[size];
            var length = 0;

            foreach (var c in encoded)
            {
                var value = Alphabet.IndexOf(c);
                if (value < 0)
                    throw new FormatException($"Invalid base58 character: {c}");

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

    /// <summary>
    /// Hex decoding.
    /// </summary>
    private static byte[] HexDecode(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Array.Empty<byte>();

        if (hex.Length % 2 != 0)
            throw new FormatException("Hex string must have an even length");

        var result = new byte[hex.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return result;
    }
}

public sealed class ATCidJsonConverter : JsonConverter<ATCid>
{
    public override ATCid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            // Simple string CID
            var value = reader.GetString();
            return new ATCid(value ?? string.Empty);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // CID-link format: { "$link": "..." }
            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.TryGetProperty("$link", out var linkProp))
            {
                var value = linkProp.GetString();
                return new ATCid(value ?? string.Empty);
            }
        }

        return new ATCid(string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, ATCid value, JsonSerializerOptions options)
    {
        // Write as CID-link format
        writer.WriteStartObject();
        writer.WriteString("$link", value.Value);
        writer.WriteEndObject();
    }
}
