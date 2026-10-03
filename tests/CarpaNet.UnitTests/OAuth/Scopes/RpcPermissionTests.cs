using System;
using CarpaNet.OAuth.Scopes;
using Xunit;

namespace CarpaNet.UnitTests.OAuth.Scopes;

/// <summary>
/// Ported from oauth-scopes scopes/rpc-permission.test.ts.
/// </summary>
public class RpcPermissionTests
{
    [Fact]
    public void Parse_Positional()
    {
        var scope = RpcPermission.Parse("rpc:com.example.service?aud=did:web:example.com%23service_id");
        Assert.Equal("did:web:example.com#service_id", scope.Aud);
        Assert.Equal(new[] { "com.example.service" }, scope.Lxm);
    }

    [Fact]
    public void Parse_QueryAndPositionalForms()
    {
        var named = RpcPermission.Parse("rpc?lxm=com.example.method1&aud=*");
        Assert.Equal("*", named.Aud);
        Assert.Equal(new[] { "com.example.method1" }, named.Lxm);

        var positional = RpcPermission.Parse("rpc:com.example.method1?aud=*");
        Assert.Equal("*", positional.Aud);
        Assert.Equal(new[] { "com.example.method1" }, positional.Lxm);
    }

    [Fact]
    public void Parse_MultipleLxm()
    {
        var scope = RpcPermission.Parse("rpc?aud=*&lxm=com.example.method1&lxm=com.example.method2");
        Assert.Equal("*", scope.Aud);
        Assert.Equal(new[] { "com.example.method1", "com.example.method2" }, scope.Lxm);
    }

    [Theory]
    // Missing lxm
    [InlineData("rpc?aud=did:web:example.com%23service_id")]
    [InlineData("rpc:?aud=did:web:example.com%23service_id")]
    [InlineData("rpc?aud=did:web:example.com")]
    // Missing aud
    [InlineData("rpc?lxm=com.example.method1")]
    [InlineData("rpc:com.example.method1")]
    [InlineData("rpc:com.example.service")]
    // lxm in both positional and query form
    [InlineData("rpc:com.example.method1?aud=did:web:example.com&lxm=com.example.method2")]
    // Any aud and any lxm
    [InlineData("rpc?aud=*&lxm=*")]
    [InlineData("rpc:*?aud=*")]
    // Invalid aud / lxm
    [InlineData("rpc:com.example.service?aud=invalid")]
    [InlineData("rpc:invalid")]
    [InlineData("rpc?lxm=invalid")]
    [InlineData("rpc:*")]
    [InlineData("invalid")]
    [InlineData("rpc:invalid?aud=did:web:example.com")]
    [InlineData("rpc:invalid?aud=did:web:example.com%23service_id")]
    [InlineData("rpc:foo.bar")]
    [InlineData("rpc:com.example.service?aud=did:web:example.com%23service_id&invalid=param")]
    [InlineData("rpc:foo.bar.baz?aud=did:web")]
    [InlineData("rpc:foo.bar.baz?aud=did:web%23service_id")]
    [InlineData("rpc:foo.bar.baz?aud=did:plc:111")]
    [InlineData("rpc:foo.bar.baz?aud=did:plc:111%23service_id")]
    [InlineData("rpc:foo.bar.baz?aud=did:foo:bar")]
    [InlineData("rpc:foo.bar.baz?aud=did:foo:bar%23service_id")]
    [InlineData("rpc:foo.bar.baz?aud=did:web:example.com%23service_id&lxm=foo.bar.baz")]
    [InlineData("rpc:foo.bar.baz?aud=invalid")]
    [InlineData("notrpc:com.example.service?aud=did:web:example.com%23service_id")]
    [InlineData("rpc?lxm=invalid&aud=invalid")]
    // Extra atproto DID rules: aud with a path, a port, an empty fragment, two auds
    [InlineData("rpc:foo.bar.baz?aud=did:web:example.com:path%23svc")]
    [InlineData("rpc:foo.bar.baz?aud=did:web:example.com%253A8080%23svc")]
    [InlineData("rpc:foo.bar.baz?aud=did:web:example.com%23")]
    [InlineData("rpc:foo.bar.baz?aud=*&aud=*")]
    public void Parse_Invalid_ReturnsFalse(string scope)
    {
        Assert.False(RpcPermission.TryParse(scope, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void Parse_ValidAtprotoDidAudiences()
    {
        Assert.True(RpcPermission.TryParse("rpc:foo.bar.baz?aud=did:plc:abcdefghijklmnopqrstuvwx%23svc", out _));
        Assert.True(RpcPermission.TryParse("rpc:foo.bar.baz?aud=did:web:localhost%253A2583%23svc", out _));
        Assert.True(RpcPermission.TryParse("rpc:app.bsky.feed.getTimeline?aud=did:web:api.bsky.app%23bsky_appview", out _));
    }

    [Fact]
    public void ScopeNeededFor()
    {
        Assert.Equal(
            "rpc:com.example.service?aud=did:web:example.com%23service_id",
            RpcPermission.ScopeNeededFor("com.example.service", "did:web:example.com#service_id"));
        Assert.Equal("rpc:com.example.method1?aud=*", RpcPermission.ScopeNeededFor("com.example.method1", "*"));
    }

    [Fact]
    public void Matches()
    {
        var exact = RpcPermission.Parse("rpc:com.example.service?aud=did:web:example.com%23service_id");
        Assert.True(exact.Matches("com.example.service", "did:web:example.com#service_id"));
        Assert.False(exact.Matches("com.example.OtherService", "did:web:example.com#service_id"));
        Assert.False(exact.Matches("com.example.service", "did:example:456#service_id"));

        Assert.True(RpcPermission.Parse("rpc:com.example.method1?aud=*")
            .Matches("com.example.method1", "did:web:example.com#service_id"));

        var anyLxm = RpcPermission.Parse("rpc:*?aud=did:web:example.com%23service_id");
        Assert.True(anyLxm.Matches("com.example.method1", "did:web:example.com#service_id"));
        Assert.True(anyLxm.Matches("com.example.anyMethod", "did:web:example.com#service_id"));
    }

    [Fact]
    public void Format()
    {
        Assert.Equal(
            "rpc:com.example.service?aud=did:web:example.com%23service_id",
            new RpcPermission("did:web:example.com#service_id", "com.example.service").ToString());
        Assert.Equal("rpc:com.example.method1?aud=*", new RpcPermission("*", "com.example.method1").ToString());
        Assert.Equal(
            "rpc?lxm=com.example.method1&lxm=com.example.method2&aud=did:web:example.com%23service_id",
            new RpcPermission("did:web:example.com#service_id", new[] { "com.example.method1", "com.example.method2" }).ToString());
        Assert.Equal(
            "rpc:*?aud=did:web:example.com%23service_id",
            new RpcPermission("did:web:example.com#service_id", "*").ToString());

        // Simplifies lxm if one of them is "*"
        Assert.Equal(
            "rpc:*?aud=did:web:example.com%23service_id",
            new RpcPermission("did:web:example.com#service_id", new[] { "*", "com.example.method1" }).ToString());
    }

    [Fact]
    public void Constructor_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => new RpcPermission("*", "*"));
        Assert.Throws<ArgumentException>(() => new RpcPermission("did:web:example.com", "com.example.foo"));
        Assert.Throws<ArgumentException>(() => new RpcPermission("*", "invalid"));
    }

    [Theory]
    [InlineData("rpc:com.example.service?aud=did:web:example.com%23service_id", "rpc:com.example.service?aud=did:web:example.com%23service_id")]
    [InlineData("rpc:com.example.service?aud=did:web:example.com#service_id", "rpc:com.example.service?aud=did:web:example.com%23service_id")]
    [InlineData("rpc?lxm=com.example.method1&lxm=com.example.method2&aud=*", "rpc?lxm=com.example.method1&lxm=com.example.method2&aud=*")]
    [InlineData(
        "rpc?lxm=com.example.method1&lxm=com.example.method2&lxm=*&aud=did:web:example.com%23service_id",
        "rpc:*?aud=did:web:example.com%23service_id")]
    [InlineData("rpc?aud=did:web:example.com%23foo&lxm=com.example.service", "rpc:com.example.service?aud=did:web:example.com%23foo")]
    [InlineData("rpc?lxm=com.example.method1&aud=did:web:example.com#foo", "rpc:com.example.method1?aud=did:web:example.com%23foo")]
    [InlineData("rpc?lxm=com.example.method1&aud=did:web:example.com%23bar", "rpc:com.example.method1?aud=did:web:example.com%23bar")]
    [InlineData("rpc:com.example.method1?&aud=*", "rpc:com.example.method1?aud=*")]
    [InlineData(
        "rpc?lxm=com.example.b&lxm=com.example.a&lxm=com.example.b&aud=*",
        "rpc?lxm=com.example.a&lxm=com.example.b&aud=*")]
    public void Reformat(string input, string expected)
    {
        Assert.Equal(expected, RpcPermission.Parse(input).ToString());
    }
}
