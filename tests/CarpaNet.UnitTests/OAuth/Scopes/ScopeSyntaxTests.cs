using CarpaNet.OAuth.Scopes;
using Xunit;

namespace CarpaNet.UnitTests.OAuth.Scopes;

/// <summary>
/// Ported from oauth-scopes lib/syntax.test.ts, lib/syntax-string.test.ts and atproto-oauth-scope.ts.
/// </summary>
public class ScopeSyntaxTests
{
    [Theory]
    [InlineData("prefix", "prefix", true)]
    [InlineData("prefix", "differentResource", false)]
    [InlineData("prefix:positional", "prefix", true)]
    [InlineData("differentResource:positional", "prefix", false)]
    [InlineData("prefix?param=value", "prefix", true)]
    [InlineData("prefix", "prefi", false)]
    [InlineData("prefix:pos", "prefi", false)]
    [InlineData("prefix?param=value", "prefi", false)]
    [InlineData("prefix", "fix", false)]
    [InlineData("prefix:pos", "fix", false)]
    [InlineData("prefix?param=value", "fix", false)]
    [InlineData("differentResource?param=value", "prefix", false)]
    public void IsScopeStringFor(string value, string prefix, bool expected)
    {
        Assert.Equal(expected, AtprotoScope.IsScopeStringFor(value, prefix));
    }

    [Theory]
    [InlineData("atproto")]
    [InlineData("transition:generic")]
    [InlineData("transition:chat.bsky")]
    [InlineData("transition:email")]
    public void StaticScopes_AreValid(string scope)
    {
        Assert.True(AtprotoScope.IsStaticScope(scope));
        Assert.True(AtprotoScope.IsValid(scope));
        Assert.Equal(scope, AtprotoScope.NormalizeValue(scope));
    }

    [Theory]
    [InlineData("account:email")]
    [InlineData("blob:image/png")]
    [InlineData("identity:handle")]
    [InlineData("include:com.example.foo")]
    [InlineData("repo:com.example.foo")]
    [InlineData("rpc:com.example.foo?aud=*")]
    public void PermissionScopes_AreValid(string scope)
    {
        Assert.True(AtprotoScope.IsValid(scope));
        Assert.True(AtprotoScope.TryParse(scope, out var parsed));
        Assert.Equal(scope, parsed!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("transition:unknown")]
    [InlineData("atproto2")]
    [InlineData("unknown:foo")]
    [InlineData("repo:invalid")]
    [InlineData("rpc:com.example.foo")]
    [InlineData("include:com.example.foo?aud=did:web:example.com")]
    public void InvalidScopes_AreNotValid(string scope)
    {
        Assert.False(AtprotoScope.IsValid(scope));
        Assert.Null(AtprotoScope.NormalizeValue(scope));
    }

    [Fact]
    public void Normalize_NormalizesSortsDropsInvalidAndDuplicates()
    {
        var normalized = AtprotoScope.Normalize(
            "repo:com.example.foo?action=create&action=update&action=delete  atproto bogus " +
            "rpc?lxm=com.example.b&lxm=com.example.a&aud=did:web:example.com#svc atproto blob?accept=image/png&accept=image/*");

        Assert.Equal(
            "atproto blob:image/* repo:com.example.foo rpc?lxm=com.example.a&lxm=com.example.b&aud=did:web:example.com%23svc",
            normalized);
    }

    [Fact]
    public void Positional_UrlEncoding_IsDecoded()
    {
        // "my-res:my%20pos" => "my pos"; checked through a permission whose positional value matters
        Assert.True(RepoPermission.TryParse("repo:com.example.f%6Fo", out var repo));
        Assert.Equal("com.example.foo", repo!.Collections[0]);
    }

    [Fact]
    public void Named_UrlEncoding_IsDecoded()
    {
        Assert.True(RpcPermission.TryParse("rpc:com.example.foo?aud=did%3Aweb%3Aexample.com%23svc", out var rpc));
        Assert.Equal("did:web:example.com#svc", rpc!.Aud);
    }

    [Fact]
    public void Positional_MalformedEncoding_IsInvalid()
    {
        Assert.False(RepoPermission.TryParse("repo:com.example.foo%zz", out _));
        Assert.False(RepoPermission.TryParse("repo:com.example.foo%2", out _));
    }

    [Fact]
    public void Query_QuestionMarkInsideParameterValue_IsPartOfValue()
    {
        // "rpc:foo.bar?aud=did:foo:bar?lxm=bar.baz" => aud is "did:foo:bar?lxm=bar.baz"
        Assert.True(RpcPermission.TryParse("rpc:foo.bar.baz?aud=did:web:example.com%23svc?lxm=x", out var rpc));
        Assert.Equal(new[] { "foo.bar.baz" }, rpc!.Lxm);
        Assert.Equal("did:web:example.com#svc?lxm=x", rpc!.Aud);
    }
}
