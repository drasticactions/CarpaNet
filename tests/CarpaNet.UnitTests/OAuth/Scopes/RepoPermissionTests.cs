using System;
using CarpaNet.OAuth.Scopes;
using Xunit;

namespace CarpaNet.UnitTests.OAuth.Scopes;

/// <summary>
/// Ported from oauth-scopes scopes/repo-permission.test.ts.
/// </summary>
public class RepoPermissionTests
{
    [Fact]
    public void Parse_Positional_DefaultsToAllActions()
    {
        var scope = RepoPermission.Parse("repo:com.example.foo");
        Assert.Equal(new[] { "com.example.foo" }, scope.Collections);
        Assert.Equal(RepoActions.All, scope.Actions);
    }

    [Fact]
    public void Parse_MultipleActions()
    {
        var scope = RepoPermission.Parse("repo:com.example.foo?action=create&action=update");
        Assert.Equal(new[] { "com.example.foo" }, scope.Collections);
        Assert.Equal(RepoActions.Create | RepoActions.Update, scope.Actions);
    }

    [Fact]
    public void Parse_WildcardCollection_WithAction()
    {
        var scope = RepoPermission.Parse("repo:*?action=create");
        Assert.Equal(new[] { "*" }, scope.Collections);
        Assert.Equal(RepoActions.Create, scope.Actions);
        Assert.True(scope.Matches("any.collection", RepoActions.Create));
        Assert.False(scope.Matches("any.collection", RepoActions.Update));
    }

    [Fact]
    public void Parse_WildcardCollection_WithoutActions()
    {
        var scope = RepoPermission.Parse("repo:*");
        Assert.Equal(new[] { "*" }, scope.Collections);
        Assert.Equal(RepoActions.All, scope.Actions);
        Assert.True(scope.Matches("any.collection", RepoActions.Create));
        Assert.True(scope.Matches("any.collection", RepoActions.Update));
        Assert.True(scope.Matches("any.collection", RepoActions.Delete));
    }

    [Theory]
    [InlineData("repo:foo bar")]
    [InlineData("repo:.foo")]
    [InlineData("repo:bar.")]
    [InlineData("repo:com.example.foo?action=invalid")]
    [InlineData("invalid")]
    [InlineData("scope")]
    [InlineData("repo:*?action=*")]
    [InlineData("repo:invalid")]
    [InlineData("repo?collection=invalid&action=invalid")]
    [InlineData("repo")]
    [InlineData("repo:")]
    [InlineData("repo:com.example.foo?collection=com.example.bar")]
    [InlineData("repo:com.example.foo?unknown=x")]
    public void Parse_Invalid_ReturnsFalse(string scope)
    {
        Assert.False(RepoPermission.TryParse(scope, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void ScopeNeededFor()
    {
        Assert.Equal("repo:com.example.foo?action=create", RepoPermission.ScopeNeededFor("com.example.foo", RepoActions.Create));
        Assert.Equal("repo:*?action=create", RepoPermission.ScopeNeededFor("*", RepoActions.Create));

        // scopeNeededFor assumes valid input and does not validate
        Assert.Equal("repo:invalid?action=create", RepoPermission.ScopeNeededFor("invalid", RepoActions.Create));
    }

    [Fact]
    public void Matches()
    {
        var create = RepoPermission.Parse("repo:com.example.foo?action=create");
        Assert.True(create.Matches("com.example.foo", RepoActions.Create));
        Assert.False(create.Matches("com.example.foo", RepoActions.Update));

        var wildcard = RepoPermission.Parse("repo:*?action=create");
        Assert.True(wildcard.Matches("com.example.bar", RepoActions.Create));
        Assert.False(wildcard.Matches("com.example.bar", RepoActions.Delete));

        var multiple = RepoPermission.Parse("repo:com.example.foo?action=create&action=update");
        Assert.True(multiple.Matches("com.example.foo", RepoActions.Create));
        Assert.True(multiple.Matches("com.example.foo", RepoActions.Update));
        Assert.False(multiple.Matches("com.example.foo", RepoActions.Delete));

        var defaulted = RepoPermission.Parse("repo:com.example.foo");
        Assert.True(defaulted.Matches("com.example.foo", RepoActions.Create));
        Assert.True(defaulted.Matches("com.example.foo", RepoActions.Update));
        Assert.True(defaulted.Matches("com.example.foo", RepoActions.Delete));
        Assert.False(defaulted.Matches("com.example.bar", RepoActions.Create));
    }

    [Fact]
    public void Format()
    {
        Assert.Equal(
            "repo:com.example.foo?action=create&action=update",
            new RepoPermission("com.example.foo", RepoActions.Create | RepoActions.Update).ToString());
        Assert.Equal("repo:com.example.foo", new RepoPermission("com.example.foo").ToString());
    }

    [Fact]
    public void Constructor_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => new RepoPermission("invalid"));
        Assert.Throws<ArgumentException>(() => new RepoPermission("com.example.foo", RepoActions.None));
        Assert.Throws<ArgumentException>(() => new RepoPermission(Array.Empty<string>()));
    }

    [Theory]
    [InlineData("repo:com.example.foo", "repo:com.example.foo")]
    [InlineData("repo:com.example.foo?action=create", "repo:com.example.foo?action=create")]
    [InlineData("repo:com.example.foo?action=create&action=update", "repo:com.example.foo?action=create&action=update")]
    [InlineData("repo:*?action=create&action=update&action=delete", "repo:*")]
    [InlineData("repo:com.example.foo?action=create&action=update&action=delete", "repo:com.example.foo")]
    [InlineData("repo:*?action=create", "repo:*?action=create")]
    [InlineData("repo:*?action=update", "repo:*?action=update")]
    [InlineData("repo?collection=*&action=update", "repo:*?action=update")]
    [InlineData("repo?collection=*&collection=com.example.foo&action=update", "repo:*?action=update")]
    [InlineData("repo?collection=*", "repo:*")]
    [InlineData("repo?collection=*&action=create&action=update&action=delete", "repo:*")]
    [InlineData("repo?collection=*&collection=com.example.foo", "repo:*")]
    [InlineData("repo?action=create&collection=com.example.foo", "repo:com.example.foo?action=create")]
    [InlineData("repo?collection=com.example.foo&action=create&action=update&action=delete", "repo:com.example.foo")]
    [InlineData(
        "repo?action=create&collection=com.example.foo&collection=com.example.bar",
        "repo?collection=com.example.bar&collection=com.example.foo&action=create")]
    [InlineData("repo:com.example.foo?action=delete&action=create", "repo:com.example.foo?action=create&action=delete")]
    public void Reformat(string input, string expected)
    {
        Assert.Equal(expected, RepoPermission.Parse(input).ToString());
    }
}
