using System;

namespace CarpaNet.OAuth.Scopes;

/// <summary>
/// Identity attributes that can be granted with an <c>identity:</c> scope.
/// </summary>
public enum IdentityAttribute
{
    /// <summary>The account handle (<c>handle</c>).</summary>
    Handle,

    /// <summary>All identity attributes, including the DID document (<c>*</c>).</summary>
    All,
}

/// <summary>
/// The <c>identity:&lt;handle|*&gt;</c> permission scope.
/// </summary>
public sealed class IdentityPermission : IAtprotoOAuthScope
{
    /// <summary>
    /// The scope prefix.
    /// </summary>
    public const string Prefix = "identity";

    /// <summary>
    /// Creates an identity permission.
    /// </summary>
    /// <param name="attribute">The identity attribute.</param>
    /// <exception cref="ArgumentException">The attribute is not valid.</exception>
    public IdentityPermission(IdentityAttribute attribute)
    {
        if (attribute != IdentityAttribute.Handle && attribute != IdentityAttribute.All)
        {
            throw new ArgumentException($"Unknown identity attribute: {attribute}", nameof(attribute));
        }

        Attribute = attribute;
    }

    /// <summary>
    /// The identity attribute.
    /// </summary>
    public IdentityAttribute Attribute { get; }

    /// <summary>
    /// Whether this permission allows access to the given attribute.
    /// </summary>
    public bool Matches(IdentityAttribute attribute)
    {
        return Attribute == IdentityAttribute.All || Attribute == attribute;
    }

    /// <summary>
    /// Parses an <c>identity:</c> scope string.
    /// </summary>
    /// <param name="scope">The scope string.</param>
    /// <param name="permission">The parsed permission, or null when invalid.</param>
    /// <returns>True when the scope is a valid <c>identity:</c> scope.</returns>
    public static bool TryParse(string? scope, out IdentityPermission? permission)
    {
        permission = null;
        if (!ScopeStringSyntax.IsScopeStringFor(scope, Prefix))
        {
            return false;
        }

        var syntax = ScopeStringSyntax.Parse(scope!);
        if (syntax == null || !syntax.HasOnlyKeys("attr"))
        {
            return false;
        }

        if (!syntax.TryGetPositionalSingle("attr", out var value))
        {
            return false;
        }

        switch (value)
        {
            case "handle":
                permission = new IdentityPermission(IdentityAttribute.Handle);
                return true;
            case "*":
                permission = new IdentityPermission(IdentityAttribute.All);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Parses an <c>identity:</c> scope string.
    /// </summary>
    /// <exception cref="FormatException">The scope is not a valid <c>identity:</c> scope.</exception>
    public static IdentityPermission Parse(string scope)
    {
        return TryParse(scope, out var permission)
            ? permission!
            : throw new FormatException($"Invalid identity scope: '{scope}'");
    }

    /// <summary>
    /// Gets the scope string needed to access the given attribute.
    /// </summary>
    public static string ScopeNeededFor(IdentityAttribute attribute)
    {
        return new IdentityPermission(attribute).ToString();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return ScopeStringSyntax.Format(Prefix, Attribute == IdentityAttribute.All ? "*" : "handle", null);
    }
}
