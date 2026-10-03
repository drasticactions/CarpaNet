using System;
using System.Collections.Generic;

namespace CarpaNet.OAuth.Scopes;

/// <summary>
/// The <c>include:&lt;nsid&gt;[?aud=&lt;did#service&gt;]</c> scope, which requests the permissions of a
/// lexicon-defined permission set. Resolving the permission set is not done by this type.
/// </summary>
public sealed class IncludeScope : IAtprotoOAuthScope
{
    /// <summary>
    /// The scope prefix.
    /// </summary>
    public const string Prefix = "include";

    /// <summary>
    /// Creates an include scope.
    /// </summary>
    /// <param name="nsid">The NSID of the permission set.</param>
    /// <param name="aud">Optional service audience (an atproto DID reference such as <c>did:web:example.com#service</c>)
    /// inherited by <c>rpc</c> permissions of the set.</param>
    /// <exception cref="ArgumentException">The NSID or audience is not valid.</exception>
    public IncludeScope(string nsid, string? aud = null)
    {
        if (!AtprotoSyntax.IsNsid(nsid))
        {
            throw new ArgumentException($"Invalid NSID: '{nsid}'", nameof(nsid));
        }

        if (aud != null && !AtprotoSyntax.IsAtprotoDidRefAbsolute(aud))
        {
            throw new ArgumentException($"Invalid include audience: '{aud}'", nameof(aud));
        }

        Nsid = nsid;
        Aud = aud;
    }

    /// <summary>
    /// The NSID of the permission set.
    /// </summary>
    public string Nsid { get; }

    /// <summary>
    /// The optional service audience.
    /// </summary>
    public string? Aud { get; }

    /// <summary>
    /// Whether the given NSID is under the namespace authority of this permission set (same NSID group,
    /// i.e. everything up to the last <c>.</c> of <see cref="Nsid"/>). A permission set may only grant
    /// permissions for NSIDs under its own authority.
    /// </summary>
    public bool IsParentAuthorityOf(string otherNsid)
    {
        if (otherNsid == null || otherNsid == ScopeHelpers.Wildcard)
        {
            return false;
        }

        var groupPrefixEnd = Nsid.LastIndexOf('.');
        if (groupPrefixEnd == -1)
        {
            return false;
        }

        // otherNsid must be longer than the group prefix (including the dot)
        if (groupPrefixEnd >= otherNsid.Length - 1)
        {
            return false;
        }

        return string.CompareOrdinal(Nsid, 0, otherNsid, 0, groupPrefixEnd + 1) == 0;
    }

    /// <summary>
    /// Parses an <c>include:</c> scope string.
    /// </summary>
    /// <param name="scope">The scope string.</param>
    /// <param name="include">The parsed scope, or null when invalid.</param>
    /// <returns>True when the scope is a valid <c>include:</c> scope.</returns>
    public static bool TryParse(string? scope, out IncludeScope? include)
    {
        include = null;
        if (!ScopeStringSyntax.IsScopeStringFor(scope, Prefix))
        {
            return false;
        }

        var syntax = ScopeStringSyntax.Parse(scope!);
        if (syntax == null || !syntax.HasOnlyKeys("nsid", "aud"))
        {
            return false;
        }

        if (!syntax.TryGetPositionalSingle("nsid", out var nsid) || nsid == null || !AtprotoSyntax.IsNsid(nsid))
        {
            return false;
        }

        if (!syntax.TryGetSingle("aud", out var aud) || (aud != null && !AtprotoSyntax.IsAtprotoDidRefAbsolute(aud)))
        {
            return false;
        }

        include = new IncludeScope(nsid, aud);
        return true;
    }

    /// <summary>
    /// Parses an <c>include:</c> scope string.
    /// </summary>
    /// <exception cref="FormatException">The scope is not a valid <c>include:</c> scope.</exception>
    public static IncludeScope Parse(string scope)
    {
        return TryParse(scope, out var include)
            ? include!
            : throw new FormatException($"Invalid include scope: '{scope}'");
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var parameters = Aud == null
            ? null
            : new[] { new KeyValuePair<string, string>("aud", Aud) };
        return ScopeStringSyntax.Format(Prefix, Nsid, parameters);
    }
}
