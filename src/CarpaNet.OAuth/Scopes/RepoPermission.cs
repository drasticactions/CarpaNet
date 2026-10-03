using System;
using System.Collections.Generic;

namespace CarpaNet.OAuth.Scopes;

/// <summary>
/// Record actions that can be granted with a <c>repo:</c> scope.
/// </summary>
[Flags]
public enum RepoActions
{
    /// <summary>No actions. Not valid in a scope.</summary>
    None = 0,

    /// <summary>Create records (<c>create</c>).</summary>
    Create = 1,

    /// <summary>Update records (<c>update</c>).</summary>
    Update = 2,

    /// <summary>Delete records (<c>delete</c>).</summary>
    Delete = 4,

    /// <summary>All actions (the default when no action is given).</summary>
    All = Create | Update | Delete,
}

/// <summary>
/// The <c>repo:&lt;collection|*&gt;[?action=create&amp;action=update&amp;action=delete]</c> permission scope.
/// </summary>
public sealed class RepoPermission : IAtprotoOAuthScope
{
    /// <summary>
    /// The scope prefix.
    /// </summary>
    public const string Prefix = "repo";

    private readonly string[] _collections;

    /// <summary>
    /// Creates a repo permission for a single collection.
    /// </summary>
    /// <param name="collection">The collection NSID, or <c>*</c> for any collection.</param>
    /// <param name="actions">The granted actions (default: all).</param>
    /// <exception cref="ArgumentException">The collection or actions are not valid.</exception>
    public RepoPermission(string collection, RepoActions actions = RepoActions.All)
        : this(new[] { collection }, actions)
    {
    }

    /// <summary>
    /// Creates a repo permission.
    /// </summary>
    /// <param name="collections">Collection NSIDs, or <c>*</c> for any collection.</param>
    /// <param name="actions">The granted actions (default: all).</param>
    /// <exception cref="ArgumentException">A collection or the actions are not valid.</exception>
    public RepoPermission(IEnumerable<string> collections, RepoActions actions = RepoActions.All)
    {
        if (actions == RepoActions.None || (actions & ~RepoActions.All) != 0)
        {
            throw new ArgumentException($"Invalid repo actions: {actions}", nameof(actions));
        }

        _collections = ScopeHelpers.ToValidatedArray(collections, IsCollection, nameof(collections));
        Actions = actions;
    }

    private RepoPermission(string[] collections, RepoActions actions, bool _)
    {
        _collections = collections;
        Actions = actions;
    }

    /// <summary>
    /// The collections, as given (<c>*</c> means any collection).
    /// </summary>
    public IReadOnlyList<string> Collections => _collections;

    /// <summary>
    /// The granted actions.
    /// </summary>
    public RepoActions Actions { get; }

    /// <summary>
    /// Whether this permission allows the given action on the given collection.
    /// </summary>
    public bool Matches(string collection, RepoActions action)
    {
        return action != RepoActions.None &&
               (Actions & action) == action &&
               (ScopeHelpers.Contains(_collections, ScopeHelpers.Wildcard) || ScopeHelpers.Contains(_collections, collection));
    }

    /// <summary>
    /// Parses a <c>repo:</c> scope string.
    /// </summary>
    /// <param name="scope">The scope string.</param>
    /// <param name="permission">The parsed permission, or null when invalid.</param>
    /// <returns>True when the scope is a valid <c>repo:</c> scope.</returns>
    public static bool TryParse(string? scope, out RepoPermission? permission)
    {
        permission = null;
        if (!ScopeStringSyntax.IsScopeStringFor(scope, Prefix))
        {
            return false;
        }

        var syntax = ScopeStringSyntax.Parse(scope!);
        if (syntax == null || !syntax.HasOnlyKeys("collection", "action"))
        {
            return false;
        }

        if (!syntax.TryGetPositionalMulti("collection", out var collections) || collections == null)
        {
            return false;
        }

        foreach (var collection in collections)
        {
            if (!IsCollection(collection))
            {
                return false;
            }
        }

        var actions = RepoActions.All;
        var actionValues = syntax.GetMulti("action");
        if (actionValues != null)
        {
            actions = RepoActions.None;
            foreach (var value in actionValues)
            {
                var action = ParseAction(value);
                if (action == RepoActions.None)
                {
                    return false;
                }

                actions |= action;
            }
        }

        permission = new RepoPermission(collections.ToArray(), actions, false);
        return true;
    }

    /// <summary>
    /// Parses a <c>repo:</c> scope string.
    /// </summary>
    /// <exception cref="FormatException">The scope is not a valid <c>repo:</c> scope.</exception>
    public static RepoPermission Parse(string scope)
    {
        return TryParse(scope, out var permission)
            ? permission!
            : throw new FormatException($"Invalid repo scope: '{scope}'");
    }

    /// <summary>
    /// Gets the scope string needed to perform the given action on the given collection. The input is not validated.
    /// </summary>
    public static string ScopeNeededFor(string collection, RepoActions action)
    {
        return new RepoPermission(new[] { collection }, action, false).ToString();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var collections = ScopeHelpers.NormalizeWildcardList(_collections);
        var parameters = new List<KeyValuePair<string, string>>();
        var positional = ScopeHelpers.AddPositionalMulti("collection", collections, parameters);

        // All actions is the default and is omitted
        if (Actions != RepoActions.All)
        {
            if ((Actions & RepoActions.Create) != 0)
            {
                parameters.Add(new KeyValuePair<string, string>("action", "create"));
            }

            if ((Actions & RepoActions.Update) != 0)
            {
                parameters.Add(new KeyValuePair<string, string>("action", "update"));
            }

            if ((Actions & RepoActions.Delete) != 0)
            {
                parameters.Add(new KeyValuePair<string, string>("action", "delete"));
            }
        }

        return ScopeStringSyntax.Format(Prefix, positional, parameters);
    }

    private static bool IsCollection(string value)
    {
        return value == ScopeHelpers.Wildcard || AtprotoSyntax.IsNsid(value);
    }

    private static RepoActions ParseAction(string value) => value switch
    {
        "create" => RepoActions.Create,
        "update" => RepoActions.Update,
        "delete" => RepoActions.Delete,
        _ => RepoActions.None,
    };
}
