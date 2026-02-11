// <copyright file="DIDValidator.cs" company="Drastic Actions">
// Copyright (c) Drastic Actions. All rights reserved.
// </copyright>

using System.Text.RegularExpressions;

namespace CarpaNet;

/// <summary>
/// Validates a DID.
/// </summary>
internal static class DIDValidator
{
    /// <summary>
    /// Ensures that the provided DID is valid.
    /// </summary>
    /// <param name="did">The DID to validate.</param>
    /// <returns><c>true</c> if the DID is valid; otherwise, <c>false</c>.</returns>
    internal static bool EnsureValidDid(string did)
    {
        if (!Regex.IsMatch(did, "^[a-zA-Z0-9._:%-]*$"))
        {
            return false;
        }

        if (!did.StartsWith("did:", StringComparison.Ordinal))
        {
            return false;
        }

        var secondColon = did.IndexOf(':', 4);

        if (secondColon == -1)
        {
            return false;
        }

#if NETSTANDARD
        var method = did.Substring(4, secondColon - 4);
#else
        var method = did.AsSpan(4, secondColon - 4);
#endif

        if (!Regex.IsMatch(method, "^[a-z]+$"))
        {
            return false;
        }

        if (did.EndsWith(":") || did.EndsWith("%"))
        {
            return false;
        }

        if (did.Length > 2 * 1024)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Ensures that the provided DID matches the regular expression pattern for DIDs.
    /// </summary>
    /// <param name="did">The DID to validate.</param>
    /// <returns><c>true</c> if the DID matches the regular expression pattern; otherwise, <c>false</c>.</returns>
    internal static bool EnsureValidDidRegex(string did)
    {
        if (!Regex.IsMatch(did, "^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._-]$"))
        {
            return false;
        }

        if (did.Length > 2 * 1024)
        {
            return false;
        }

        return true;
    }
}