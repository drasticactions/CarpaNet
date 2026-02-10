using System;
using System.Formats.Cbor;
using CarpaNet.Cbor;

namespace CarpaNet.Repo;

/// <summary>
/// Represents a signed commit object in an ATProtocol repository.
/// The commit is the top-level object that references the MST root.
/// </summary>
public sealed class RepoCommit
{
    /// <summary>
    /// The account DID associated with this repository.
    /// </summary>
    public string Did { get; set; } = string.Empty;

    /// <summary>
    /// The repository format version (should be 3 for current format).
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// The CID pointing to the root of the MST data structure.
    /// </summary>
    public ATCid Data { get; set; }

    /// <summary>
    /// The revision of the repo (TID format), used as a logical clock.
    /// </summary>
    public string Rev { get; set; } = string.Empty;

    /// <summary>
    /// Pointer to a previous commit (nullable, typically null in v3).
    /// </summary>
    public ATCid? Prev { get; set; }

    /// <summary>
    /// The cryptographic signature of this commit.
    /// </summary>
    public byte[] Sig { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Parses a RepoCommit from DAG-CBOR data.
    /// </summary>
    public static RepoCommit FromCbor(byte[] data)
    {
        var reader = new DagCborReader(data);
        return FromCbor(ref reader);
    }

    /// <summary>
    /// Parses a RepoCommit from a DagCborReader.
    /// </summary>
    public static RepoCommit FromCbor(ref DagCborReader reader)
    {
        var commit = new RepoCommit();
        var count = reader.ReadStartMap();

        var remaining = count ?? int.MaxValue;
        while (remaining > 0 && reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();

            switch (key)
            {
                case "did":
                    commit.Did = reader.ReadTextString();
                    break;

                case "version":
                    commit.Version = reader.ReadInt32();
                    break;

                case "data":
                    commit.Data = reader.ReadCidLink();
                    break;

                case "rev":
                    commit.Rev = reader.ReadTextString();
                    break;

                case "prev":
                    if (reader.PeekState() == CborReaderState.Null)
                    {
                        reader.ReadNull();
                        commit.Prev = null;
                    }
                    else
                    {
                        commit.Prev = reader.ReadCidLink();
                    }
                    break;

                case "sig":
                    commit.Sig = reader.ReadByteString();
                    break;

                default:
                    reader.SkipValue();
                    break;
            }

            remaining--;
        }

        reader.ReadEndMap();
        return commit;
    }
}
