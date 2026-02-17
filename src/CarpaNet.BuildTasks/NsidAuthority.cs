using System;
using System.Linq;

namespace CarpaNet.BuildTasks;

/// <summary>
/// Converts ATProtocol NSIDs to DNS authority names for lexicon resolution.
/// </summary>
/// <remarks>
/// Per the ATProtocol lexicon resolution spec:
/// NSID "com.example.myapp.getProfile" → authority "com.example.myapp" → DNS name "_lexicon.myapp.example.com"
/// The authority is the NSID minus the last segment, and the DNS name reverses the authority segments.
/// </remarks>
internal static class NsidAuthority
{
    /// <summary>
    /// Validates that a string is a well-formed NSID (at least 3 dot-separated segments,
    /// each segment alphanumeric/hyphen, first segment doesn't start with digit).
    /// </summary>
    public static bool IsValidNsid(string nsid)
    {
        if (string.IsNullOrWhiteSpace(nsid))
            return false;

        var segments = nsid.Split('.');
        if (segments.Length < 3)
            return false;

        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment) || segment.Length > 63)
                return false;

            if (!char.IsLetterOrDigit(segment[0]) || !char.IsLetterOrDigit(segment[segment.Length - 1]))
                return false;

            foreach (var c in segment)
            {
                if (!char.IsLetterOrDigit(c) && c != '-')
                    return false;
            }
        }

        // First segment (TLD in reversed form) must not start with digit
        if (char.IsDigit(segments[0][0]))
            return false;

        return true;
    }

    /// <summary>
    /// Extracts the authority from an NSID (everything except the last segment).
    /// Example: "com.example.myapp.getProfile" → "com.example.myapp"
    /// </summary>
    public static string GetAuthority(string nsid)
    {
        var lastDot = nsid.LastIndexOf('.');
        if (lastDot <= 0)
            throw new ArgumentException($"Invalid NSID format: '{nsid}'", nameof(nsid));

        return nsid.Substring(0, lastDot);
    }

    /// <summary>
    /// Converts an NSID authority to its DNS name for lexicon lookup.
    /// The authority segments are reversed and prefixed with "_lexicon.".
    /// Example: "com.example.myapp" → "_lexicon.myapp.example.com"
    /// </summary>
    public static string AuthorityToDnsName(string authority)
    {
        var segments = authority.Split('.');
        Array.Reverse(segments);
        return "_lexicon." + string.Join(".", segments);
    }

    /// <summary>
    /// Converts an NSID directly to its DNS lookup name.
    /// Combines <see cref="GetAuthority"/> and <see cref="AuthorityToDnsName"/>.
    /// Example: "com.example.myapp.getProfile" → "_lexicon.myapp.example.com"
    /// </summary>
    public static string NsidToDnsName(string nsid)
    {
        return AuthorityToDnsName(GetAuthority(nsid));
    }
}
