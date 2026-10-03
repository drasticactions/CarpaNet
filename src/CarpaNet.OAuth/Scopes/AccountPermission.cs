using System;
using System.Collections.Generic;

namespace CarpaNet.OAuth.Scopes;

/// <summary>
/// Account attributes that can be granted with an <c>account:</c> scope.
/// </summary>
public enum AccountAttribute
{
    /// <summary>The account email (<c>email</c>).</summary>
    Email,

    /// <summary>The account repository (<c>repo</c>), e.g. for import.</summary>
    Repo,

    /// <summary>The account status (<c>status</c>), e.g. activation.</summary>
    Status,
}

/// <summary>
/// Actions that can be granted with an <c>account:</c> scope.
/// </summary>
[Flags]
public enum AccountActions
{
    /// <summary>No actions. Not valid in a scope.</summary>
    None = 0,

    /// <summary>Read the attribute (<c>read</c>, the default).</summary>
    Read = 1,

    /// <summary>Manage (and read) the attribute (<c>manage</c>).</summary>
    Manage = 2,
}

/// <summary>
/// The <c>account:&lt;attr&gt;[?action=read|manage]</c> permission scope.
/// </summary>
public sealed class AccountPermission : IAtprotoOAuthScope
{
    /// <summary>
    /// The scope prefix.
    /// </summary>
    public const string Prefix = "account";

    /// <summary>
    /// Creates an account permission.
    /// </summary>
    /// <param name="attribute">The account attribute.</param>
    /// <param name="actions">The granted actions (default: <see cref="AccountActions.Read"/>).</param>
    /// <exception cref="ArgumentException">The attribute or actions are not valid.</exception>
    public AccountPermission(AccountAttribute attribute, AccountActions actions = AccountActions.Read)
    {
        if (ToValue(attribute) == null)
        {
            throw new ArgumentException($"Unknown account attribute: {attribute}", nameof(attribute));
        }

        if (actions == AccountActions.None || (actions & ~(AccountActions.Read | AccountActions.Manage)) != 0)
        {
            throw new ArgumentException($"Invalid account actions: {actions}", nameof(actions));
        }

        Attribute = attribute;
        Actions = actions;
    }

    /// <summary>
    /// The account attribute.
    /// </summary>
    public AccountAttribute Attribute { get; }

    /// <summary>
    /// The granted actions.
    /// </summary>
    public AccountActions Actions { get; }

    /// <summary>
    /// Whether this permission allows the given action on the given attribute.
    /// <see cref="AccountActions.Manage"/> implies <see cref="AccountActions.Read"/>.
    /// </summary>
    public bool Matches(AccountAttribute attribute, AccountActions action)
    {
        return Attribute == attribute &&
               ((Actions & AccountActions.Manage) != 0 || (action != AccountActions.None && (Actions & action) == action));
    }

    /// <summary>
    /// Parses an <c>account:</c> scope string.
    /// </summary>
    /// <param name="scope">The scope string.</param>
    /// <param name="permission">The parsed permission, or null when invalid.</param>
    /// <returns>True when the scope is a valid <c>account:</c> scope.</returns>
    public static bool TryParse(string? scope, out AccountPermission? permission)
    {
        permission = null;
        if (!ScopeStringSyntax.IsScopeStringFor(scope, Prefix))
        {
            return false;
        }

        var syntax = ScopeStringSyntax.Parse(scope!);
        if (syntax == null || !syntax.HasOnlyKeys("attr", "action"))
        {
            return false;
        }

        if (!syntax.TryGetPositionalSingle("attr", out var attrValue) || attrValue == null)
        {
            return false;
        }

        var attribute = ParseAttribute(attrValue);
        if (attribute == null)
        {
            return false;
        }

        var actions = AccountActions.Read;
        var actionValues = syntax.GetMulti("action");
        if (actionValues != null)
        {
            actions = AccountActions.None;
            foreach (var value in actionValues)
            {
                switch (value)
                {
                    case "read":
                        actions |= AccountActions.Read;
                        break;
                    case "manage":
                        actions |= AccountActions.Manage;
                        break;
                    default:
                        return false;
                }
            }
        }

        permission = new AccountPermission(attribute.Value, actions);
        return true;
    }

    /// <summary>
    /// Parses an <c>account:</c> scope string.
    /// </summary>
    /// <exception cref="FormatException">The scope is not a valid <c>account:</c> scope.</exception>
    public static AccountPermission Parse(string scope)
    {
        return TryParse(scope, out var permission)
            ? permission!
            : throw new FormatException($"Invalid account scope: '{scope}'");
    }

    /// <summary>
    /// Gets the scope string needed to perform the given action on the given attribute.
    /// </summary>
    public static string ScopeNeededFor(AccountAttribute attribute, AccountActions action)
    {
        return new AccountPermission(attribute, action).ToString();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        List<KeyValuePair<string, string>>? parameters = null;

        // "read" alone is the default and is omitted
        if (Actions != AccountActions.Read)
        {
            parameters = new List<KeyValuePair<string, string>>(2);
            if ((Actions & AccountActions.Read) != 0)
            {
                parameters.Add(new KeyValuePair<string, string>("action", "read"));
            }

            if ((Actions & AccountActions.Manage) != 0)
            {
                parameters.Add(new KeyValuePair<string, string>("action", "manage"));
            }
        }

        return ScopeStringSyntax.Format(Prefix, ToValue(Attribute), parameters);
    }

    private static AccountAttribute? ParseAttribute(string value) => value switch
    {
        "email" => AccountAttribute.Email,
        "repo" => AccountAttribute.Repo,
        "status" => AccountAttribute.Status,
        _ => null,
    };

    private static string? ToValue(AccountAttribute attribute) => attribute switch
    {
        AccountAttribute.Email => "email",
        AccountAttribute.Repo => "repo",
        AccountAttribute.Status => "status",
        _ => null,
    };
}
