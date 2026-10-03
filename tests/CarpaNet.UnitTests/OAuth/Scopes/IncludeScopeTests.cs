using System;
using CarpaNet.OAuth.Scopes;
using Xunit;

namespace CarpaNet.UnitTests.OAuth.Scopes;

/// <summary>
/// Ported from oauth-scopes scopes/include-scope.test.ts (parsing, formatting and
/// isParentAuthorityOf; permission-set expansion is not implemented).
/// </summary>
public class IncludeScopeTests
{
    [Theory]
    [InlineData("include:com.example.bar", "com.example.bar", null)]
    [InlineData("include:com.example.baz?aud=did:web:example.com%23my_service", "com.example.baz", "did:web:example.com#my_service")]
    [InlineData("include:com.example.baz?aud=did:web:example.com#my_service", "com.example.baz", "did:web:example.com#my_service")]
    [InlineData("include?nsid=com.example.baz", "com.example.baz", null)]
    [InlineData("include?aud=did:web:example.com%23my_service&nsid=com.example.baz", "com.example.baz", "did:web:example.com#my_service")]
    public void Parse_Valid(string scope, string nsid, string? aud)
    {
        var include = IncludeScope.Parse(scope);
        Assert.Equal(nsid, include.Nsid);
        Assert.Equal(aud, include.Aud);
    }

    [Theory]
    [InlineData("")]
    [InlineData("repo:com.example.baz")]
    [InlineData("include")]
    [InlineData("include#")]
    // Invalid NSID
    [InlineData("include:")]
    [InlineData("include:#")]
    [InlineData("include:&")]
    [InlineData("include:com..example")]
    [InlineData("include:com")]
    [InlineData("include:com.example")]
    [InlineData("include:9com.example.foo")]
    [InlineData("include:com.example.-bar")]
    [InlineData("include:invalid^nsid")]
    [InlineData("include:nsid")]
    // Invalid AUD
    [InlineData("include:com.example.baz?aud=")]
    [InlineData("include:com.example.baz?aud=did:web:example.com")]
    [InlineData("include:com.example.baz?aud=invalid^did")]
    // Duplicate or unknown params
    [InlineData("include:com.example.baz?nsid=com.example.baz")]
    [InlineData("include:com.example.baz?aud=did:web:a.com%23x&aud=did:web:b.com%23x")]
    [InlineData("include:com.example.baz?lxm=com.example.baz")]
    public void Parse_Invalid_ReturnsFalse(string scope)
    {
        Assert.False(IncludeScope.TryParse(scope, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Format()
    {
        Assert.Equal("include:com.example.foo", new IncludeScope("com.example.foo").ToString());
        Assert.Equal(
            "include:com.example.foo?aud=did:web:example.com%23my_service",
            new IncludeScope("com.example.foo", "did:web:example.com#my_service").ToString());
        Assert.Equal(
            "include:com.example.baz?aud=did:web:example.com%23my_service",
            IncludeScope.Parse("include?aud=did:web:example.com%23my_service&nsid=com.example.baz").ToString());
    }

    [Fact]
    public void Constructor_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => new IncludeScope("nsid"));
        Assert.Throws<ArgumentException>(() => new IncludeScope("com.example.foo", "did:web:example.com"));
    }

    [Theory]
    [InlineData("com.example.foo.identifier", true)]
    [InlineData("com.example.foo.bar.baz", true)]
    [InlineData("com.example.foo.bar.baz.quz", true)]
    [InlineData("com", false)]
    [InlineData("com.example", false)]
    [InlineData("com.example.bar", false)]
    [InlineData("com.example.bar.foo", false)]
    [InlineData("com.example.bar.qux", false)]
    [InlineData("com.atproto.foo", false)]
    [InlineData("com.atproto.foo.auth", false)]
    [InlineData("com.atproto.foo.bar", false)]
    [InlineData("*", false)]
    public void IsParentAuthorityOf(string nsid, bool expected)
    {
        var scope = new IncludeScope("com.example.foo.auth");
        Assert.Equal(expected, scope.IsParentAuthorityOf(nsid));
    }
}
