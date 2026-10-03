using System;
using System.Collections.Generic;
using System.Text;

namespace CarpaNet.OAuth.Scopes;

/// <summary>
/// Parsing, validation and normalization of atproto OAuth scope values. Port of
/// <c>@atproto/oauth-scopes</c> (without permission-set resolution).
/// </summary>
public static class AtprotoScope
{
    /// <summary>
    /// The <c>atproto</c> scope, required in every atproto OAuth request.
    /// </summary>
    public const string Atproto = "atproto";

    /// <summary>
    /// The <c>transition:generic</c> scope: broad access similar to an app password.
    /// </summary>
    public const string TransitionGeneric = "transition:generic";

    /// <summary>
    /// The <c>transition:chat.bsky</c> scope: access to <c>chat.bsky.*</c> methods (with <see cref="TransitionGeneric"/>).
    /// </summary>
    public const string TransitionChatBsky = "transition:chat.bsky";

    /// <summary>
    /// The <c>transition:email</c> scope: read access to the account email.
    /// </summary>
    public const string TransitionEmail = "transition:email";

    /// <summary>
    /// Whether the value is one of the static scopes (<c>atproto</c> and the <c>transition:</c> scopes).
    /// </summary>
    public static bool IsStaticScope(string? value)
    {
        return value == Atproto ||
               value == TransitionGeneric ||
               value == TransitionChatBsky ||
               value == TransitionEmail;
    }

    /// <summary>
    /// Whether the value is for the given scope prefix (resource): equal to it, or followed by <c>:</c> or <c>?</c>.
    /// </summary>
    /// <param name="value">The scope value.</param>
    /// <param name="prefix">The prefix, e.g. <c>repo</c>.</param>
    public static bool IsScopeStringFor(string? value, string prefix)
    {
        if (prefix == null)
        {
            throw new ArgumentNullException(nameof(prefix));
        }

        return ScopeStringSyntax.IsScopeStringFor(value, prefix);
    }

    /// <summary>
    /// Whether a single scope value is a valid atproto scope: a static scope, or a permission scope whose
    /// parameters are all valid and understood.
    /// </summary>
    public static bool IsValid(string? value)
    {
        return IsStaticScope(value) || TryParse(value, out _);
    }

    /// <summary>
    /// Parses a single permission scope value (<c>account</c>, <c>blob</c>, <c>identity</c>, <c>include</c>,
    /// <c>repo</c> or <c>rpc</c>). Static scopes such as <c>atproto</c> are not permission scopes and return false.
    /// </summary>
    /// <param name="value">The scope value.</param>
    /// <param name="scope">The parsed scope (for example a <see cref="RepoPermission"/>), or null.</param>
    /// <returns>True when the value is a valid permission scope.</returns>
    public static bool TryParse(string? value, out IAtprotoOAuthScope? scope)
    {
        scope = null;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        // Dispatch on the first char to avoid trying every parser
        switch (value![0])
        {
            case 'a' when AccountPermission.TryParse(value, out var account):
                scope = account;
                return true;
            case 'b' when BlobPermission.TryParse(value, out var blob):
                scope = blob;
                return true;
            case 'i' when IdentityPermission.TryParse(value, out var identity):
                scope = identity;
                return true;
            case 'i' when IncludeScope.TryParse(value, out var include):
                scope = include;
                return true;
            case 'r' when RepoPermission.TryParse(value, out var repo):
                scope = repo;
                return true;
            case 'r' when RpcPermission.TryParse(value, out var rpc):
                scope = rpc;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Normalizes a single scope value.
    /// </summary>
    /// <returns>The normalized value, or null when the value is not a valid atproto scope.</returns>
    public static string? NormalizeValue(string? value)
    {
        if (IsStaticScope(value))
        {
            return value;
        }

        return TryParse(value, out var scope) ? scope!.ToString() : null;
    }

    /// <summary>
    /// Normalizes a space-separated scope string: each value is normalized, invalid values are dropped,
    /// duplicates are removed and the result is sorted (ordinal).
    /// </summary>
    /// <param name="scope">The space-separated scope string.</param>
    /// <returns>The normalized space-separated scope string.</returns>
    public static string Normalize(string? scope)
    {
        if (string.IsNullOrEmpty(scope))
        {
            return string.Empty;
        }

        var values = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var value in scope!.Split(' '))
        {
            var normalized = NormalizeValue(value);
            if (normalized != null)
            {
                values.Add(normalized);
            }
        }

        return Join(values);
    }

    internal static string Join(IEnumerable<string> values)
    {
        var sb = new StringBuilder();
        foreach (var value in values)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(value);
        }

        return sb.ToString();
    }
}
