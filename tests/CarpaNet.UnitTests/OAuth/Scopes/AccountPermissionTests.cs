using System;
using CarpaNet.OAuth.Scopes;
using Xunit;

namespace CarpaNet.UnitTests.OAuth.Scopes;

/// <summary>
/// Ported from oauth-scopes scopes/account-permission.test.ts.
/// </summary>
public class AccountPermissionTests
{
    [Fact]
    public void Parse_ValidScopes()
    {
        Assert.True(AccountPermission.TryParse("account:email?action=read", out var scope1));
        Assert.Equal(AccountAttribute.Email, scope1!.Attribute);
        Assert.Equal(AccountActions.Read, scope1.Actions);

        Assert.True(AccountPermission.TryParse("account:repo?action=manage", out var scope2));
        Assert.Equal(AccountAttribute.Repo, scope2!.Attribute);
        Assert.Equal(AccountActions.Manage, scope2.Actions);
    }

    [Fact]
    public void Parse_WithoutAction_DefaultsToRead()
    {
        var scope = AccountPermission.Parse("account:status");
        Assert.Equal(AccountAttribute.Status, scope.Attribute);
        Assert.Equal(AccountActions.Read, scope.Actions);
    }

    [Theory]
    [InlineData("account:invalid")]
    [InlineData("account:email?action=invalid")]
    [InlineData("invalid:email")]
    [InlineData("account")]
    [InlineData("")]
    [InlineData("account:")]
    [InlineData("account:email?attr=repo")]
    [InlineData("account:email?unknown=1")]
    public void Parse_Invalid_ReturnsFalse(string scope)
    {
        Assert.False(AccountPermission.TryParse(scope, out var parsed));
        Assert.Null(parsed);
        Assert.Throws<FormatException>(() => AccountPermission.Parse(scope));
    }

    [Theory]
    [InlineData(AccountAttribute.Email, AccountActions.Read, "account:email")]
    [InlineData(AccountAttribute.Repo, AccountActions.Read, "account:repo")]
    [InlineData(AccountAttribute.Status, AccountActions.Read, "account:status")]
    [InlineData(AccountAttribute.Email, AccountActions.Manage, "account:email?action=manage")]
    [InlineData(AccountAttribute.Repo, AccountActions.Manage, "account:repo?action=manage")]
    [InlineData(AccountAttribute.Status, AccountActions.Manage, "account:status?action=manage")]
    public void ScopeNeededFor(AccountAttribute attribute, AccountActions action, string expected)
    {
        Assert.Equal(expected, AccountPermission.ScopeNeededFor(attribute, action));
    }

    [Fact]
    public void Matches()
    {
        Assert.True(AccountPermission.Parse("account:email?action=read").Matches(AccountAttribute.Email, AccountActions.Read));
        Assert.True(AccountPermission.Parse("account:repo?action=manage").Matches(AccountAttribute.Repo, AccountActions.Manage));
        Assert.False(AccountPermission.Parse("account:email?action=read").Matches(AccountAttribute.Email, AccountActions.Manage));
        Assert.False(AccountPermission.Parse("account:email?action=read").Matches(AccountAttribute.Repo, AccountActions.Read));

        var defaulted = AccountPermission.Parse("account:email");
        Assert.True(defaulted.Matches(AccountAttribute.Email, AccountActions.Read));
        Assert.False(defaulted.Matches(AccountAttribute.Email, AccountActions.Manage));

        Assert.True(AccountPermission.Parse("account:status?action=read").Matches(AccountAttribute.Status, AccountActions.Read));

        // "manage" implies "read"
        Assert.True(AccountPermission.Parse("account:email?action=manage").Matches(AccountAttribute.Email, AccountActions.Read));
    }

    [Fact]
    public void Format()
    {
        Assert.Equal("account:email?action=manage", new AccountPermission(AccountAttribute.Email, AccountActions.Manage).ToString());
        Assert.Equal("account:repo", new AccountPermission(AccountAttribute.Repo, AccountActions.Read).ToString());
        Assert.Equal("account:email", new AccountPermission(AccountAttribute.Email).ToString());
        Assert.Equal("account:status", new AccountPermission(AccountAttribute.Status).ToString());
        Assert.Equal(
            "account:email?action=read&action=manage",
            new AccountPermission(AccountAttribute.Email, AccountActions.Read | AccountActions.Manage).ToString());
    }

    [Fact]
    public void Constructor_RejectsInvalidActions()
    {
        Assert.Throws<ArgumentException>(() => new AccountPermission(AccountAttribute.Email, AccountActions.None));
        Assert.Throws<ArgumentException>(() => new AccountPermission((AccountAttribute)42));
    }

    [Theory]
    [InlineData("account:email")]
    [InlineData("account:email?action=manage")]
    [InlineData("account:repo")]
    [InlineData("account:repo?action=manage")]
    [InlineData("account:status")]
    [InlineData("account:status?action=manage")]
    public void RoundTrip(string scope)
    {
        Assert.Equal(scope, AccountPermission.Parse(scope).ToString());
    }

    [Fact]
    public void QueryForm_IsNormalizedToPositional()
    {
        Assert.Equal("account:email?action=manage", AccountPermission.Parse("account?attr=email&action=manage").ToString());
        Assert.Equal("account:email", AccountPermission.Parse("account:email?action=read").ToString());
    }
}
