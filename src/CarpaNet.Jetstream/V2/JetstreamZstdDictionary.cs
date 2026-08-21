using System.Buffers.Binary;

namespace CarpaNet.Jetstream;

/// <summary>
/// Parses the header of a structured zstd dictionary (RFC 8878 §5). The client fetches the
/// dictionary blob via getZstdDictionary and needs its embedded ID for the zstdDictionary
/// websocket negotiation parameter.
/// </summary>
internal static class JetstreamZstdDictionary
{
    /// <summary>The structured-dictionary magic number (RFC 8878 §5), little-endian.</summary>
    private const uint Magic = 0xEC30A437;

    /// <summary>
    /// Extracts the dictionary ID from a structured zstd dictionary blob. Returns false for
    /// raw/content-only dictionaries (no header), which the Jetstream wire contract does not use.
    /// </summary>
    public static bool TryParseId(byte[]? dictionary, out uint id)
    {
        id = 0;
        if (dictionary == null || dictionary.Length < 8)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(dictionary.AsSpan(0, 4)) != Magic)
        {
            return false;
        }

        id = BinaryPrimitives.ReadUInt32LittleEndian(dictionary.AsSpan(4, 4));
        return id != 0;
    }
}
