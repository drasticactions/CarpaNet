using CarpaNet.OAuth.Scopes;
using Xunit;

namespace CarpaNet.UnitTests.OAuth.Scopes;

/// <summary>
/// Ported from oauth-scopes scopes/identity-permission.test.ts.
/// </summary>
public class IdentityPermissionTests
{
    [Fact]
    public void Parse_Positional()
    {
        Assert.Equal(IdentityAttribute.Handle, IdentityPermission.Parse("identity:handle").Attribute);
        Assert.Equal(IdentityAttribute.All, IdentityPermission.Parse("identity:*").Attribute);
        Assert.Equal(IdentityAttribute.Handle, IdentityPermission.Parse("identity?attr=handle").Attribute);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("identity:invalid")]
    [InlineData("identity:*?action=*")]
    [InlineData("identity:*?action=manage")]
    [InlineData("identity:*?action=submit")]
    [InlineData("identity:handle?action=invalid")]
    [InlineData("identity?attribute=invalid&action=invalid")]
    [InlineData("identity")]
    [InlineData("identity:handle?attr=handle")]
    public void Parse_Invalid_ReturnsFalse(string scope)
    {
        Assert.False(IdentityPermission.TryParse(scope, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void ScopeNeededFor()
    {
        Assert.Equal("identity:handle", IdentityPermission.ScopeNeededFor(IdentityAttribute.Handle));
        Assert.Equal("identity:*", IdentityPermission.ScopeNeededFor(IdentityAttribute.All));
    }

    [Fact]
    public void Matches()
    {
        var handle = IdentityPermission.Parse("identity:handle");
        Assert.True(handle.Matches(IdentityAttribute.Handle));
        Assert.False(handle.Matches(IdentityAttribute.All));

        var all = IdentityPermission.Parse("identity:*");
        Assert.True(all.Matches(IdentityAttribute.All));
        Assert.True(all.Matches(IdentityAttribute.Handle));
    }

    [Fact]
    public void Format()
    {
        Assert.Equal("identity:handle", new IdentityPermission(IdentityAttribute.Handle).ToString());
        Assert.Equal("identity:*", new IdentityPermission(IdentityAttribute.All).ToString());
    }
}
