using System;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Scopes;
using Xunit;

namespace CarpaNet.UnitTests.OAuth.Scopes;

/// <summary>
/// Ported from oauth-scopes scopes-set.test.ts, plus builder tests.
/// </summary>
public class ScopeSetTests
{
    [Fact]
    public void NewSet_IsEmpty()
    {
        var set = new ScopeSet();
        Assert.Empty(set);
        Assert.Equal(string.Empty, set.ToString());
    }

    [Fact]
    public void Add_And_Remove()
    {
        var set = new ScopeSet().Add("repo:read");
        Assert.Single(set);
        Assert.True(set.Contains("repo:read"));
        Assert.False(set.Contains("repo:write"));

        Assert.True(set.Remove("repo:read"));
        Assert.Empty(set);
        Assert.False(set.Contains("repo:read"));
        Assert.False(set.Remove("repo:read"));
    }

    [Fact]
    public void MatchesRepo()
    {
        var set = new ScopeSet(new[] { "repo:com.example.foo" });
        Assert.True(set.MatchesRepo("com.example.foo", RepoActions.Create));
        Assert.False(set.MatchesRepo("com.example.bar", RepoActions.Create));

        var createOnly = new ScopeSet(new[] { "repo:com.example.foo?action=create" });
        Assert.False(createOnly.MatchesRepo("com.example.foo", RepoActions.Delete));

        var invalid = new ScopeSet(new[] { "repo:not-a-valid-nsid" });
        Assert.False(invalid.MatchesRepo("not-a-valid-nsid", RepoActions.Create));
    }

    [Fact]
    public void Matches_OtherResources()
    {
        var set = ScopeSet.Parse(
            "atproto rpc:app.bsky.actor.getProfile?aud=did:web:api.bsky.app%23bsky_appview blob:image/* account:email identity:handle");

        Assert.True(set.MatchesRpc("app.bsky.actor.getProfile", "did:web:api.bsky.app#bsky_appview"));
        Assert.False(set.MatchesRpc("app.bsky.actor.getProfiles", "did:web:api.bsky.app#bsky_appview"));
        Assert.True(set.MatchesBlob("image/png"));
        Assert.False(set.MatchesBlob("video/mp4"));
        Assert.True(set.MatchesAccount(AccountAttribute.Email, AccountActions.Read));
        Assert.False(set.MatchesAccount(AccountAttribute.Email, AccountActions.Manage));
        Assert.True(set.MatchesIdentity(IdentityAttribute.Handle));
        Assert.False(set.MatchesIdentity(IdentityAttribute.All));
    }

    [Fact]
    public void Builder_ProducesSpaceSeparatedString()
    {
        var set = new ScopeSet()
            .AddAtproto()
            .AddRepo("app.bsky.feed.post", RepoActions.Create)
            .AddRepo("app.bsky.feed.like")
            .AddBlob("image/*", "video/mp4")
            .AddRpc("app.bsky.actor.getProfile", "did:web:api.bsky.app#bsky_appview")
            .AddAccount(AccountAttribute.Email)
            .AddIdentity(IdentityAttribute.Handle)
            .AddInclude("com.example.authBasic", "did:web:example.com#svc")
            .AddTransitionGeneric()
            .AddAtproto(); // duplicate is ignored

        Assert.Equal(
            "atproto repo:app.bsky.feed.post?action=create repo:app.bsky.feed.like blob?accept=image/*&accept=video/mp4 " +
            "rpc:app.bsky.actor.getProfile?aud=did:web:api.bsky.app%23bsky_appview account:email identity:handle " +
            "include:com.example.authBasic?aud=did:web:example.com%23svc transition:generic",
            set.ToString());

        foreach (var value in set)
        {
            Assert.True(AtprotoScope.IsValid(value), value);
        }
    }

    [Fact]
    public void Parse_KeepsUnknownValues_AndNormalizedStringDropsThem()
    {
        var set = ScopeSet.Parse("transition:generic  atproto future:thing");
        Assert.Equal(3, set.Count);
        Assert.True(set.Contains("future:thing"));
        Assert.Equal("transition:generic atproto future:thing", set.ToString());
        Assert.Equal("atproto transition:generic", set.ToNormalizedString());
    }

    [Fact]
    public void Add_RejectsWhitespaceAndEmpty()
    {
        Assert.Throws<ArgumentException>(() => new ScopeSet().Add("atproto transition:generic"));
        Assert.Throws<ArgumentException>(() => new ScopeSet().Add(string.Empty));
    }

    [Fact]
    public void OAuthClientConfig_SetScope()
    {
        var config = new OAuthClientConfig()
            .SetScope(new ScopeSet().AddAtproto().AddTransitionGeneric());
        Assert.Equal("atproto transition:generic", config.Scope);

        Assert.Throws<ArgumentException>(() => new OAuthClientConfig().SetScope(new ScopeSet().AddTransitionGeneric()));
    }
}
