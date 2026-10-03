using System;
using System.Collections.Generic;
using System.Text;

namespace CarpaNet.Cbor;

/// <summary>
/// Orders map keys the way DAG-CBOR requires: shorter UTF-8 encodings first, then bytewise
/// comparison of the UTF-8 bytes for keys of equal length (RFC 7049 section 3.9 canonical order).
/// </summary>
/// <remarks>
/// Content identifiers (CIDs) of atproto records are computed over this canonical encoding,
/// so every map that DAG-CBOR writers emit must use this order.
/// </remarks>
public sealed class DagCborKeyComparer : IComparer<string>
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static DagCborKeyComparer Instance { get; } = new();

    private DagCborKeyComparer()
    {
    }

    /// <inheritdoc/>
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x == null)
        {
            return -1;
        }

        if (y == null)
        {
            return 1;
        }

        var xBytes = Encoding.UTF8.GetBytes(x);
        var yBytes = Encoding.UTF8.GetBytes(y);

        if (xBytes.Length != yBytes.Length)
        {
            return xBytes.Length < yBytes.Length ? -1 : 1;
        }

        for (var i = 0; i < xBytes.Length; i++)
        {
            if (xBytes[i] != yBytes[i])
            {
                return xBytes[i] < yBytes[i] ? -1 : 1;
            }
        }

        return 0;
    }
}
