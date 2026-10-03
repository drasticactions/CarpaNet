using System;
using System.Collections.Generic;

namespace CarpaNet.OAuth.Scopes;

/// <summary>
/// The <c>blob:&lt;mime pattern&gt;</c> (or <c>blob?accept=...&amp;accept=...</c>) permission scope.
/// </summary>
public sealed class BlobPermission : IAtprotoOAuthScope
{
    /// <summary>
    /// The scope prefix.
    /// </summary>
    public const string Prefix = "blob";

    private const string AnyMime = "*/*";

    private readonly string[] _accept;

    /// <summary>
    /// Creates a blob permission.
    /// </summary>
    /// <param name="accept">Accepted MIME patterns: <c>*/*</c>, <c>type/*</c> or <c>type/subtype</c>.</param>
    /// <exception cref="ArgumentException">No pattern, or an invalid pattern, was given.</exception>
    public BlobPermission(params string[] accept)
        : this((IEnumerable<string>)accept)
    {
    }

    /// <summary>
    /// Creates a blob permission.
    /// </summary>
    /// <param name="accept">Accepted MIME patterns: <c>*/*</c>, <c>type/*</c> or <c>type/subtype</c>.</param>
    /// <exception cref="ArgumentException">No pattern, or an invalid pattern, was given.</exception>
    public BlobPermission(IEnumerable<string> accept)
    {
        _accept = ScopeHelpers.ToValidatedArray(accept, IsAccept, nameof(accept));
    }

    private BlobPermission(string[] accept, bool _)
    {
        _accept = accept;
    }

    /// <summary>
    /// The accepted MIME patterns, as given.
    /// </summary>
    public IReadOnlyList<string> Accept => _accept;

    /// <summary>
    /// Whether this permission allows uploading a blob of the given MIME type.
    /// </summary>
    public bool Matches(string mime)
    {
        return MatchesAnyAccept(_accept, mime);
    }

    /// <summary>
    /// Whether the value is a concrete MIME type (<c>type/subtype</c>, no wildcard).
    /// </summary>
    public static bool IsMime(string? value)
    {
        return IsStringSlashString(value) && value!.IndexOf('*') == -1;
    }

    /// <summary>
    /// Whether the value is a valid accept pattern: <c>*/*</c>, <c>type/*</c> or <c>type/subtype</c>.
    /// </summary>
    public static bool IsAccept(string? value)
    {
        if (value == AnyMime)
        {
            return true;
        }

        if (!IsStringSlashString(value))
        {
            return false;
        }

        return value!.IndexOf('*') == -1 || value.EndsWith("/*", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the MIME type matches the accept pattern. Returns false for an invalid MIME type.
    /// </summary>
    public static bool MatchesAccept(string accept, string mime)
    {
        return IsMime(mime) && MatchesAcceptUnsafe(accept, mime);
    }

    /// <summary>
    /// Whether the MIME type matches any of the accept patterns. Returns false for an invalid MIME type.
    /// </summary>
    public static bool MatchesAnyAccept(IEnumerable<string> accept, string mime)
    {
        if (!IsMime(mime))
        {
            return false;
        }

        foreach (var pattern in accept)
        {
            if (MatchesAcceptUnsafe(pattern, mime))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses a <c>blob:</c> scope string.
    /// </summary>
    /// <param name="scope">The scope string.</param>
    /// <param name="permission">The parsed permission, or null when invalid.</param>
    /// <returns>True when the scope is a valid <c>blob:</c> scope.</returns>
    public static bool TryParse(string? scope, out BlobPermission? permission)
    {
        permission = null;
        if (!ScopeStringSyntax.IsScopeStringFor(scope, Prefix))
        {
            return false;
        }

        var syntax = ScopeStringSyntax.Parse(scope!);
        if (syntax == null || !syntax.HasOnlyKeys("accept"))
        {
            return false;
        }

        if (!syntax.TryGetPositionalMulti("accept", out var accept) || accept == null)
        {
            return false;
        }

        foreach (var value in accept)
        {
            if (!IsAccept(value))
            {
                return false;
            }
        }

        permission = new BlobPermission(accept.ToArray(), false);
        return true;
    }

    /// <summary>
    /// Parses a <c>blob:</c> scope string.
    /// </summary>
    /// <exception cref="FormatException">The scope is not a valid <c>blob:</c> scope.</exception>
    public static BlobPermission Parse(string scope)
    {
        return TryParse(scope, out var permission)
            ? permission!
            : throw new FormatException($"Invalid blob scope: '{scope}'");
    }

    /// <summary>
    /// Gets the scope string needed to upload a blob of the given MIME type. The input is not validated.
    /// </summary>
    public static string ScopeNeededFor(string mime)
    {
        return new BlobPermission(new[] { mime }, false).ToString();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var normalized = Normalize(_accept);
        var parameters = new List<KeyValuePair<string, string>>(normalized.Length > 1 ? normalized.Length : 0);
        var positional = ScopeHelpers.AddPositionalMulti("accept", normalized, parameters);
        return ScopeStringSyntax.Format(Prefix, positional, parameters);
    }

    private static string[] Normalize(string[] accept)
    {
        // A more concise representation of the accept values
        if (ScopeHelpers.Contains(accept, AnyMime))
        {
            return new[] { AnyMime };
        }

        var lower = new string[accept.Length];
        for (var i = 0; i < accept.Length; i++)
        {
            lower[i] = accept[i].ToLowerInvariant();
        }

        // Drop "type/subtype" values made redundant by a "type/*" value
        var kept = new List<string>(lower.Length);
        foreach (var value in lower)
        {
            if (!value.EndsWith("/*", StringComparison.Ordinal))
            {
                var slash = value.IndexOf('/');
                var wildcard = value.Substring(0, slash) + "/*";
                if (ScopeHelpers.Contains(lower, wildcard))
                {
                    continue;
                }
            }

            kept.Add(value);
        }

        return ScopeHelpers.SortedUnique(kept);
    }

    private static bool MatchesAcceptUnsafe(string accept, string mime)
    {
        if (accept == AnyMime)
        {
            return true;
        }

        if (accept.EndsWith("/*", StringComparison.Ordinal))
        {
            return mime.StartsWith(accept.Substring(0, accept.Length - 1), StringComparison.Ordinal);
        }

        return string.Equals(accept, mime, StringComparison.Ordinal);
    }

    private static bool IsStringSlashString(string? value)
    {
        if (value == null)
        {
            return false;
        }

        var slash = value.IndexOf('/');
        return slash > 0 &&
               slash < value.Length - 1 &&
               value.IndexOf('/', slash + 1) == -1 &&
               value.IndexOf(' ') == -1;
    }
}
