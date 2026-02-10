using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Text;
using CarpaNet.Cbor;

namespace CarpaNet.Repo;

/// <summary>
/// Represents a node in the Merkle Search Tree (MST).
/// The MST is used to store the key/value mapping of repository paths to record CIDs.
/// </summary>
public sealed class MstNode
{
    /// <summary>
    /// Link to the left sub-tree (all keys sorting before keys at this node).
    /// Null if there is no left sub-tree.
    /// </summary>
    public ATCid? Left { get; set; }

    /// <summary>
    /// Ordered list of tree entries at this node.
    /// </summary>
    public List<MstEntry> Entries { get; set; } = new();

    /// <summary>
    /// Parses an MstNode from DAG-CBOR data.
    /// </summary>
    public static MstNode FromCbor(byte[] data)
    {
        var reader = new DagCborReader(data);
        return FromCbor(ref reader);
    }

    /// <summary>
    /// Parses an MstNode from a DagCborReader.
    /// </summary>
    public static MstNode FromCbor(ref DagCborReader reader)
    {
        var node = new MstNode();
        var count = reader.ReadStartMap();

        var remaining = count ?? int.MaxValue;
        while (remaining > 0 && reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();

            switch (key)
            {
                case "l": // left
                    if (reader.PeekState() == CborReaderState.Null)
                    {
                        reader.ReadNull();
                        node.Left = null;
                    }
                    else
                    {
                        node.Left = reader.ReadCidLink();
                    }
                    break;

                case "e": // entries
                    var entryCount = reader.ReadStartArray();
                    var entryRemaining = entryCount ?? int.MaxValue;
                    byte[] previousKey = Array.Empty<byte>();

                    while (entryRemaining > 0 && reader.PeekState() != CborReaderState.EndArray)
                    {
                        var entry = MstEntry.FromCbor(ref reader, previousKey);
                        node.Entries.Add(entry);
                        previousKey = entry.KeyBytes;
                        entryRemaining--;
                    }
                    reader.ReadEndArray();
                    break;

                default:
                    reader.SkipValue();
                    break;
            }

            remaining--;
        }

        reader.ReadEndMap();
        return node;
    }

    /// <summary>
    /// Enumerates all key/value pairs in this node (without traversing sub-trees).
    /// </summary>
    public IEnumerable<(string Key, ATCid Value)> GetEntries()
    {
        foreach (var entry in Entries)
        {
            yield return (entry.Key, entry.Value);
        }
    }
}

/// <summary>
/// Represents a single entry in an MST node.
/// </summary>
public sealed class MstEntry
{
    /// <summary>
    /// The full key bytes (reconstructed from prefix compression).
    /// </summary>
    public byte[] KeyBytes { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The full key as a string (UTF-8 decoded).
    /// This is the repository path in format "collection/recordKey".
    /// </summary>
    public string Key => Encoding.UTF8.GetString(KeyBytes);

    /// <summary>
    /// The CID link to the record data.
    /// </summary>
    public ATCid Value { get; set; }

    /// <summary>
    /// Link to a sub-tree with keys between this entry and the next.
    /// Null if there is no sub-tree.
    /// </summary>
    public ATCid? Tree { get; set; }

    /// <summary>
    /// Parses an MstEntry from a DagCborReader.
    /// </summary>
    /// <param name="reader">The CBOR reader.</param>
    /// <param name="previousKey">The previous entry's key (for prefix decompression).</param>
    internal static MstEntry FromCbor(ref DagCborReader reader, byte[] previousKey)
    {
        var entry = new MstEntry();
        int prefixLen = 0;
        byte[] keySuffix = Array.Empty<byte>();

        var count = reader.ReadStartMap();
        var remaining = count ?? int.MaxValue;

        while (remaining > 0 && reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();

            switch (key)
            {
                case "p": // prefixlen
                    prefixLen = reader.ReadInt32();
                    break;

                case "k": // keysuffix
                    keySuffix = reader.ReadByteString();
                    break;

                case "v": // value (CID link to record)
                    entry.Value = reader.ReadCidLink();
                    break;

                case "t": // tree (CID link to sub-tree)
                    if (reader.PeekState() == CborReaderState.Null)
                    {
                        reader.ReadNull();
                        entry.Tree = null;
                    }
                    else
                    {
                        entry.Tree = reader.ReadCidLink();
                    }
                    break;

                default:
                    reader.SkipValue();
                    break;
            }

            remaining--;
        }

        reader.ReadEndMap();

        // Reconstruct the full key from prefix + suffix
        entry.KeyBytes = new byte[prefixLen + keySuffix.Length];
        if (prefixLen > 0 && previousKey.Length >= prefixLen)
        {
            Array.Copy(previousKey, entry.KeyBytes, prefixLen);
        }
        Array.Copy(keySuffix, 0, entry.KeyBytes, prefixLen, keySuffix.Length);

        return entry;
    }

    /// <summary>
    /// Gets the collection name from the key (e.g., "app.bsky.feed.post").
    /// </summary>
    public string? Collection
    {
        get
        {
            var slashIndex = Key.IndexOf('/');
            return slashIndex > 0 ? Key.Substring(0, slashIndex) : null;
        }
    }

    /// <summary>
    /// Gets the record key from the key (e.g., "3k2yihcrp6f2c").
    /// </summary>
    public string? RecordKey
    {
        get
        {
            var slashIndex = Key.IndexOf('/');
            return slashIndex >= 0 && slashIndex < Key.Length - 1 ? Key.Substring(slashIndex + 1) : null;
        }
    }
}
