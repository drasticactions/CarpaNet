using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CarpaNet.Cbor;

namespace CarpaNet.Repo;

/// <summary>
/// Represents an ATProtocol repository loaded from a CAR file.
/// Provides access to the commit, MST structure, and records.
/// </summary>
public sealed class Repository
{
    private readonly Dictionary<string, byte[]> _blocks;
    private readonly CarHeader _header;
    private RepoCommit? _commit;
    private Dictionary<string, RepositoryRecord>? _records;

    /// <summary>
    /// Creates a Repository from a CAR file.
    /// </summary>
    private Repository(CarHeader header, Dictionary<string, byte[]> blocks)
    {
        _header = header;
        _blocks = blocks;
    }

    /// <summary>
    /// Loads a repository from a CAR file stream.
    /// </summary>
    public static Repository Load(Stream stream)
    {
        using var reader = new CarReader(stream, leaveOpen: true);
        var header = reader.Header;
        var blocks = reader.ReadAllBlocks();
        return new Repository(header, blocks);
    }

    /// <summary>
    /// Loads a repository from CAR file bytes.
    /// </summary>
    public static Repository Load(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return Load(stream);
    }

    /// <summary>
    /// Loads a repository from a CAR file path.
    /// </summary>
    public static Repository LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>
    /// Gets the root CID from the CAR header.
    /// </summary>
    public ATCid RootCid => _header.Roots.FirstOrDefault();

    /// <summary>
    /// Gets all root CIDs from the CAR header.
    /// </summary>
    public IReadOnlyList<ATCid> Roots => _header.Roots;

    /// <summary>
    /// Gets the commit object.
    /// </summary>
    public RepoCommit Commit
    {
        get
        {
            if (_commit == null)
            {
                var rootCid = RootCid;
                if (!_blocks.TryGetValue(rootCid.Value, out var commitData))
                    throw new InvalidOperationException($"Commit block not found: {rootCid.Value}");

                _commit = RepoCommit.FromCbor(commitData);
            }
            return _commit;
        }
    }

    /// <summary>
    /// Gets the DID of the repository owner.
    /// </summary>
    public string Did => Commit.Did;

    /// <summary>
    /// Gets the current revision of the repository.
    /// </summary>
    public string Rev => Commit.Rev;

    /// <summary>
    /// Gets the repository format version.
    /// </summary>
    public int Version => Commit.Version;

    /// <summary>
    /// Gets all records in the repository, keyed by path (collection/recordKey).
    /// </summary>
    public IReadOnlyDictionary<string, RepositoryRecord> Records
    {
        get
        {
            if (_records == null)
            {
                _records = new Dictionary<string, RepositoryRecord>();
                foreach (var (path, cid) in EnumerateRecordPaths())
                {
                    if (_blocks.TryGetValue(cid.Value, out var data))
                    {
                        _records[path] = new RepositoryRecord(path, cid, data);
                    }
                }
            }
            return _records;
        }
    }

    /// <summary>
    /// Gets the number of blocks in the repository.
    /// </summary>
    public int BlockCount => _blocks.Count;

    /// <summary>
    /// Gets a block by CID.
    /// </summary>
    /// <param name="cid">The CID of the block.</param>
    /// <returns>The block data, or null if not found.</returns>
    public byte[]? GetBlock(ATCid cid)
    {
        return _blocks.TryGetValue(cid.Value, out var data) ? data : null;
    }

    /// <summary>
    /// Gets a block by CID string.
    /// </summary>
    public byte[]? GetBlock(string cidString)
    {
        return _blocks.TryGetValue(cidString, out var data) ? data : null;
    }

    /// <summary>
    /// Tries to get a record by path.
    /// </summary>
    /// <param name="collection">The collection NSID (e.g., "app.bsky.feed.post").</param>
    /// <param name="recordKey">The record key (e.g., "3k2yihcrp6f2c").</param>
    /// <param name="record">The record if found.</param>
    /// <returns>True if the record was found.</returns>
    public bool TryGetRecord(string collection, string recordKey, out RepositoryRecord? record)
    {
        var path = $"{collection}/{recordKey}";
        if (Records.TryGetValue(path, out var r))
        {
            record = r;
            return true;
        }
        record = null;
        return false;
    }

    /// <summary>
    /// Gets all records in a specific collection.
    /// </summary>
    /// <param name="collection">The collection NSID (e.g., "app.bsky.feed.post").</param>
    /// <returns>All records in the collection.</returns>
    public IEnumerable<RepositoryRecord> GetRecordsByCollection(string collection)
    {
        var prefix = collection + "/";
        return Records.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                      .Select(kv => kv.Value);
    }

    /// <summary>
    /// Gets all unique collection names in the repository.
    /// </summary>
    public IEnumerable<string> GetCollections()
    {
        return Records.Keys
            .Select(path =>
            {
                var slashIndex = path.IndexOf('/');
                return slashIndex > 0 ? path.Substring(0, slashIndex) : path;
            })
            .Distinct()
            .OrderBy(c => c);
    }

    /// <summary>
    /// Enumerates all record paths and their CIDs by traversing the MST.
    /// </summary>
    private IEnumerable<(string Path, ATCid Cid)> EnumerateRecordPaths()
    {
        var mstRootCid = Commit.Data;
        if (string.IsNullOrEmpty(mstRootCid.Value))
            yield break;

        // BFS traversal of the MST
        var visited = new HashSet<string>();
        var queue = new Queue<ATCid>();
        queue.Enqueue(mstRootCid);

        while (queue.Count > 0)
        {
            var nodeCid = queue.Dequeue();
            if (visited.Contains(nodeCid.Value))
                continue;
            visited.Add(nodeCid.Value);

            if (!_blocks.TryGetValue(nodeCid.Value, out var nodeData))
                continue;

            MstNode node;
            try
            {
                node = MstNode.FromCbor(nodeData);
            }
            catch
            {
                // Not an MST node, might be a record or other data
                continue;
            }

            // Enqueue left sub-tree
            if (node.Left != null && !string.IsNullOrEmpty(node.Left.Value.Value))
            {
                queue.Enqueue(node.Left.Value);
            }

            // Process entries
            foreach (var entry in node.Entries)
            {
                yield return (entry.Key, entry.Value);

                // Enqueue right sub-tree
                if (entry.Tree != null && !string.IsNullOrEmpty(entry.Tree.Value.Value))
                {
                    queue.Enqueue(entry.Tree.Value);
                }
            }
        }
    }

    /// <summary>
    /// Gets an MST node by CID.
    /// </summary>
    public MstNode? GetMstNode(ATCid cid)
    {
        if (!_blocks.TryGetValue(cid.Value, out var data))
            return null;

        try
        {
            return MstNode.FromCbor(data);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the root MST node.
    /// </summary>
    public MstNode? GetRootMstNode()
    {
        return GetMstNode(Commit.Data);
    }

    /// <summary>
    /// Computes the depth of a key in the MST using SHA-256.
    /// </summary>
    /// <param name="key">The key string (UTF-8 encoded for hashing).</param>
    /// <returns>The depth value (number of leading zero bit pairs).</returns>
    public static int ComputeMstDepth(string key)
    {
        return ComputeMstDepth(Encoding.UTF8.GetBytes(key));
    }

    /// <summary>
    /// Computes the depth of a key in the MST using SHA-256.
    /// </summary>
    /// <param name="keyBytes">The key bytes.</param>
    /// <returns>The depth value (number of leading zero bit pairs).</returns>
    public static int ComputeMstDepth(byte[] keyBytes)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(keyBytes);

        // Count leading zero bits, divided by 2 (2-bit chunks)
        int leadingZeroBits = 0;
        foreach (var b in hash)
        {
            if (b == 0)
            {
                leadingZeroBits += 8;
            }
            else
            {
                // Count leading zeros in this byte
                for (int i = 7; i >= 0; i--)
                {
                    if ((b & (1 << i)) == 0)
                        leadingZeroBits++;
                    else
                        break;
                }
                break;
            }
        }

        return leadingZeroBits / 2;
    }
}

/// <summary>
/// Represents a record in the repository.
/// </summary>
public sealed class RepositoryRecord
{
    /// <summary>
    /// The path of the record (collection/recordKey).
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The CID of the record.
    /// </summary>
    public ATCid Cid { get; }

    /// <summary>
    /// The raw DAG-CBOR data of the record.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    /// Creates a new RepositoryRecord.
    /// </summary>
    public RepositoryRecord(string path, ATCid cid, byte[] data)
    {
        Path = path;
        Cid = cid;
        Data = data;
    }

    /// <summary>
    /// Gets the collection name (e.g., "app.bsky.feed.post").
    /// </summary>
    public string Collection
    {
        get
        {
            var slashIndex = Path.IndexOf('/');
            return slashIndex > 0 ? Path.Substring(0, slashIndex) : Path;
        }
    }

    /// <summary>
    /// Gets the record key (e.g., "3k2yihcrp6f2c").
    /// </summary>
    public string RecordKey
    {
        get
        {
            var slashIndex = Path.IndexOf('/');
            return slashIndex >= 0 && slashIndex < Path.Length - 1 ? Path.Substring(slashIndex + 1) : string.Empty;
        }
    }

    /// <summary>
    /// Gets the $type field from the record data, if present.
    /// </summary>
    public string? Type
    {
        get
        {
            try
            {
                var reader = new DagCborReader(Data);
                var count = reader.ReadStartMap();
                var remaining = count ?? int.MaxValue;

                while (remaining > 0 && reader.PeekState() != System.Formats.Cbor.CborReaderState.EndMap)
                {
                    var key = reader.ReadTextString();
                    if (key == "$type")
                    {
                        return reader.ReadTextString();
                    }
                    reader.SkipValue();
                    remaining--;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Reads the record data as a CBOR reader for further parsing.
    /// </summary>
    public DagCborReader GetReader() => new DagCborReader(Data);
}
