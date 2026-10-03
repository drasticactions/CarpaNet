using System;
using System.Collections;
using System.Collections.Generic;

namespace CarpaNet.OAuth.Scopes;

/// <summary>
/// An ordered set of OAuth scope values, used to build the space-separated <c>scope</c> parameter
/// (see <see cref="OAuthClientConfig.SetScope(ScopeSet)"/>) and to check granted scopes.
/// </summary>
/// <example>
/// <code>
/// var scopes = new ScopeSet()
///     .AddAtproto()
///     .AddRepo("app.bsky.feed.post", RepoActions.Create)
///     .AddBlob("image/*")
///     .AddRpc("app.bsky.actor.getProfile", "did:web:api.bsky.app#bsky_appview");
/// config.SetScope(scopes);
/// // "atproto repo:app.bsky.feed.post?action=create blob:image/* rpc:app.bsky.actor.getProfile?aud=did:web:api.bsky.app%23bsky_appview"
/// </code>
/// </example>
public sealed class ScopeSet : IReadOnlyCollection<string>
{
    private readonly List<string> _values = new();
    private readonly HashSet<string> _set = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates an empty scope set.
    /// </summary>
    public ScopeSet()
    {
    }

    /// <summary>
    /// Creates a scope set from scope values.
    /// </summary>
    /// <param name="scopes">The scope values (each without spaces).</param>
    public ScopeSet(IEnumerable<string> scopes)
    {
        if (scopes == null)
        {
            throw new ArgumentNullException(nameof(scopes));
        }

        foreach (var scope in scopes)
        {
            Add(scope);
        }
    }

    /// <summary>
    /// Parses a space-separated scope string (e.g. a token response's <c>scope</c>). Values are kept as-is,
    /// including values this library does not understand.
    /// </summary>
    public static ScopeSet Parse(string? scope)
    {
        var set = new ScopeSet();
        if (!string.IsNullOrEmpty(scope))
        {
            foreach (var value in scope!.Split(' '))
            {
                if (value.Length > 0)
                {
                    set.AddCore(value);
                }
            }
        }

        return set;
    }

    /// <inheritdoc/>
    public int Count => _values.Count;

    /// <summary>
    /// Adds a raw scope value. Unknown values are allowed, for forward compatibility.
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty or contains whitespace.</exception>
    public ScopeSet Add(string scope)
    {
        if (string.IsNullOrEmpty(scope))
        {
            throw new ArgumentException("Scope value cannot be empty.", nameof(scope));
        }

        foreach (var c in scope)
        {
            if (char.IsWhiteSpace(c))
            {
                throw new ArgumentException($"Scope value cannot contain whitespace: '{scope}'", nameof(scope));
            }
        }

        AddCore(scope);
        return this;
    }

    /// <summary>
    /// Adds a parsed scope, formatted in its normalized form.
    /// </summary>
    public ScopeSet Add(IAtprotoOAuthScope scope)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        AddCore(scope.ToString());
        return this;
    }

    /// <summary>
    /// Adds the <c>atproto</c> scope (required in every request).
    /// </summary>
    public ScopeSet AddAtproto() => AddCore(AtprotoScope.Atproto);

    /// <summary>
    /// Adds the <c>transition:generic</c> scope.
    /// </summary>
    public ScopeSet AddTransitionGeneric() => AddCore(AtprotoScope.TransitionGeneric);

    /// <summary>
    /// Adds the <c>transition:chat.bsky</c> scope.
    /// </summary>
    public ScopeSet AddTransitionChatBsky() => AddCore(AtprotoScope.TransitionChatBsky);

    /// <summary>
    /// Adds the <c>transition:email</c> scope.
    /// </summary>
    public ScopeSet AddTransitionEmail() => AddCore(AtprotoScope.TransitionEmail);

    /// <summary>
    /// Adds a <c>repo:</c> permission.
    /// </summary>
    /// <param name="collection">The collection NSID, or <c>*</c>.</param>
    /// <param name="actions">The granted actions (default: all).</param>
    public ScopeSet AddRepo(string collection, RepoActions actions = RepoActions.All) =>
        Add(new RepoPermission(collection, actions));

    /// <summary>
    /// Adds an <c>rpc:</c> permission.
    /// </summary>
    /// <param name="lxm">The method NSID, or <c>*</c>.</param>
    /// <param name="aud">The service DID reference (e.g. <c>did:web:api.bsky.app#bsky_appview</c>), or <c>*</c>.</param>
    public ScopeSet AddRpc(string lxm, string aud) => Add(new RpcPermission(aud, lxm));

    /// <summary>
    /// Adds a <c>blob:</c> permission.
    /// </summary>
    /// <param name="accept">Accepted MIME patterns, e.g. <c>image/*</c>.</param>
    public ScopeSet AddBlob(params string[] accept) => Add(new BlobPermission(accept));

    /// <summary>
    /// Adds an <c>account:</c> permission.
    /// </summary>
    public ScopeSet AddAccount(AccountAttribute attribute, AccountActions actions = AccountActions.Read) =>
        Add(new AccountPermission(attribute, actions));

    /// <summary>
    /// Adds an <c>identity:</c> permission.
    /// </summary>
    public ScopeSet AddIdentity(IdentityAttribute attribute) => Add(new IdentityPermission(attribute));

    /// <summary>
    /// Adds an <c>include:</c> scope.
    /// </summary>
    /// <param name="nsid">The permission set NSID.</param>
    /// <param name="aud">Optional service DID reference.</param>
    public ScopeSet AddInclude(string nsid, string? aud = null) => Add(new IncludeScope(nsid, aud));

    /// <summary>
    /// Removes a scope value.
    /// </summary>
    /// <returns>True when the value was present.</returns>
    public bool Remove(string scope)
    {
        if (scope == null || !_set.Remove(scope))
        {
            return false;
        }

        _values.Remove(scope);
        return true;
    }

    /// <summary>
    /// Whether the set contains exactly this scope value.
    /// </summary>
    public bool Contains(string scope) => scope != null && _set.Contains(scope);

    /// <summary>
    /// Whether any <c>repo:</c> scope in the set allows the action on the collection.
    /// </summary>
    public bool MatchesRepo(string collection, RepoActions action)
    {
        foreach (var value in _values)
        {
            if (RepoPermission.TryParse(value, out var p) && p!.Matches(collection, action))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether any <c>rpc:</c> scope in the set allows calling the method on the service.
    /// </summary>
    public bool MatchesRpc(string lxm, string aud)
    {
        foreach (var value in _values)
        {
            if (RpcPermission.TryParse(value, out var p) && p!.Matches(lxm, aud))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether any <c>blob:</c> scope in the set allows uploading the MIME type.
    /// </summary>
    public bool MatchesBlob(string mime)
    {
        foreach (var value in _values)
        {
            if (BlobPermission.TryParse(value, out var p) && p!.Matches(mime))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether any <c>account:</c> scope in the set allows the action on the attribute.
    /// </summary>
    public bool MatchesAccount(AccountAttribute attribute, AccountActions action)
    {
        foreach (var value in _values)
        {
            if (AccountPermission.TryParse(value, out var p) && p!.Matches(attribute, action))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether any <c>identity:</c> scope in the set allows access to the attribute.
    /// </summary>
    public bool MatchesIdentity(IdentityAttribute attribute)
    {
        foreach (var value in _values)
        {
            if (IdentityPermission.TryParse(value, out var p) && p!.Matches(attribute))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the normalized scope string: values normalized, invalid values dropped, sorted.
    /// </summary>
    public string ToNormalizedString() => AtprotoScope.Normalize(ToString());

    /// <summary>
    /// Returns the space-separated scope string, in insertion order.
    /// </summary>
    public override string ToString() => AtprotoScope.Join(_values);

    /// <inheritdoc/>
    public IEnumerator<string> GetEnumerator() => _values.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private ScopeSet AddCore(string scope)
    {
        if (_set.Add(scope))
        {
            _values.Add(scope);
        }

        return this;
    }
}
