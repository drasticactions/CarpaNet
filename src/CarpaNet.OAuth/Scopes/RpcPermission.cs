using System;
using System.Collections.Generic;

namespace CarpaNet.OAuth.Scopes;

/// <summary>
/// The <c>rpc:&lt;lxm|*&gt;?aud=&lt;did#service|*&gt;</c> (or <c>rpc?lxm=...&amp;lxm=...&amp;aud=...</c>) permission scope,
/// which allows calling XRPC methods on a service through the PDS (service proxying).
/// </summary>
public sealed class RpcPermission : IAtprotoOAuthScope
{
    /// <summary>
    /// The scope prefix.
    /// </summary>
    public const string Prefix = "rpc";

    private readonly string[] _lxm;

    /// <summary>
    /// Creates an RPC permission for a single method.
    /// </summary>
    /// <param name="aud">The service: an atproto DID reference such as <c>did:web:api.bsky.app#bsky_appview</c>, or <c>*</c>.</param>
    /// <param name="lxm">The method NSID, or <c>*</c> for any method.</param>
    /// <exception cref="ArgumentException">The audience or method is not valid, or both are wildcards.</exception>
    public RpcPermission(string aud, string lxm)
        : this(aud, new[] { lxm })
    {
    }

    /// <summary>
    /// Creates an RPC permission.
    /// </summary>
    /// <param name="aud">The service: an atproto DID reference such as <c>did:web:api.bsky.app#bsky_appview</c>, or <c>*</c>.</param>
    /// <param name="lxm">Method NSIDs, or <c>*</c> for any method.</param>
    /// <exception cref="ArgumentException">The audience or a method is not valid, or both are wildcards.</exception>
    public RpcPermission(string aud, IEnumerable<string> lxm)
    {
        if (!IsAud(aud))
        {
            throw new ArgumentException($"Invalid rpc audience: '{aud}'", nameof(aud));
        }

        _lxm = ScopeHelpers.ToValidatedArray(lxm, IsLxm, nameof(lxm));

        if (aud == ScopeHelpers.Wildcard && ScopeHelpers.Contains(_lxm, ScopeHelpers.Wildcard))
        {
            throw new ArgumentException("rpc:*?aud=* is not allowed.", nameof(lxm));
        }

        Aud = aud;
    }

    private RpcPermission(string aud, string[] lxm, bool _)
    {
        Aud = aud;
        _lxm = lxm;
    }

    /// <summary>
    /// The service audience (<c>*</c> means any service).
    /// </summary>
    public string Aud { get; }

    /// <summary>
    /// The lexicon methods, as given (<c>*</c> means any method).
    /// </summary>
    public IReadOnlyList<string> Lxm => _lxm;

    /// <summary>
    /// Whether this permission allows calling the given method on the given service.
    /// </summary>
    public bool Matches(string lxm, string aud)
    {
        return (Aud == ScopeHelpers.Wildcard || string.Equals(Aud, aud, StringComparison.Ordinal)) &&
               (ScopeHelpers.Contains(_lxm, ScopeHelpers.Wildcard) || ScopeHelpers.Contains(_lxm, lxm));
    }

    /// <summary>
    /// Parses an <c>rpc:</c> scope string.
    /// </summary>
    /// <param name="scope">The scope string.</param>
    /// <param name="permission">The parsed permission, or null when invalid.</param>
    /// <returns>True when the scope is a valid <c>rpc:</c> scope.</returns>
    public static bool TryParse(string? scope, out RpcPermission? permission)
    {
        permission = null;
        if (!ScopeStringSyntax.IsScopeStringFor(scope, Prefix))
        {
            return false;
        }

        var syntax = ScopeStringSyntax.Parse(scope!);
        if (syntax == null || !syntax.HasOnlyKeys("lxm", "aud"))
        {
            return false;
        }

        if (!syntax.TryGetPositionalMulti("lxm", out var lxm) || lxm == null)
        {
            return false;
        }

        foreach (var value in lxm)
        {
            if (!IsLxm(value))
            {
                return false;
            }
        }

        if (!syntax.TryGetSingle("aud", out var aud) || aud == null || !IsAud(aud))
        {
            return false;
        }

        // rpc:*?aud=* is forbidden
        if (aud == ScopeHelpers.Wildcard && ScopeHelpers.Contains(lxm, ScopeHelpers.Wildcard))
        {
            return false;
        }

        permission = new RpcPermission(aud, lxm.ToArray(), false);
        return true;
    }

    /// <summary>
    /// Parses an <c>rpc:</c> scope string.
    /// </summary>
    /// <exception cref="FormatException">The scope is not a valid <c>rpc:</c> scope.</exception>
    public static RpcPermission Parse(string scope)
    {
        return TryParse(scope, out var permission)
            ? permission!
            : throw new FormatException($"Invalid rpc scope: '{scope}'");
    }

    /// <summary>
    /// Gets the scope string needed to call the given method on the given service. The input is not validated.
    /// </summary>
    public static string ScopeNeededFor(string lxm, string aud)
    {
        return new RpcPermission(aud, new[] { lxm }, false).ToString();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var lxm = ScopeHelpers.NormalizeWildcardList(_lxm);
        var parameters = new List<KeyValuePair<string, string>>(lxm.Length + 1);
        var positional = ScopeHelpers.AddPositionalMulti("lxm", lxm, parameters);
        parameters.Add(new KeyValuePair<string, string>("aud", Aud));
        return ScopeStringSyntax.Format(Prefix, positional, parameters);
    }

    private static bool IsLxm(string value)
    {
        return value == ScopeHelpers.Wildcard || AtprotoSyntax.IsNsid(value);
    }

    private static bool IsAud(string? value)
    {
        return value == ScopeHelpers.Wildcard || AtprotoSyntax.IsAtprotoDidRefAbsolute(value);
    }
}
