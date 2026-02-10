using System;
using System.Threading.Tasks;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using Xunit;

namespace CarpaNet.UnitTests.OAuth;

public class OAuthStorageTests
{
    [Fact]
    public async Task MemoryStateStore_StoreAndConsume_Works()
    {
        var store = new MemoryOAuthStateStore();
        var state = Pkce.GenerateState();
        var data = new OAuthStateData
        {
            Issuer = "https://example.com",
            Verifier = Pkce.GenerateVerifier(),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        await store.StoreAsync(state, data);
        var retrieved = await store.ConsumeAsync(state);

        Assert.NotNull(retrieved);
        Assert.Equal(data.Issuer, retrieved!.Issuer);
        Assert.Equal(data.Verifier, retrieved.Verifier);
    }

    [Fact]
    public async Task MemoryStateStore_Consume_RemovesState()
    {
        var store = new MemoryOAuthStateStore();
        var state = Pkce.GenerateState();
        var data = new OAuthStateData
        {
            Issuer = "https://example.com",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        await store.StoreAsync(state, data);
        await store.ConsumeAsync(state); // First consume

        var secondAttempt = await store.ConsumeAsync(state);

        Assert.Null(secondAttempt);
    }

    [Fact]
    public async Task MemoryStateStore_ExpiredState_ReturnsNull()
    {
        var store = new MemoryOAuthStateStore();
        var state = Pkce.GenerateState();
        var data = new OAuthStateData
        {
            Issuer = "https://example.com",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) // Already expired
        };

        await store.StoreAsync(state, data);
        var retrieved = await store.ConsumeAsync(state);

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task MemorySessionStore_StoreAndGet_Works()
    {
        var store = new MemoryOAuthSessionStore();
        var sub = "did:plc:test123";
        var data = new OAuthSessionData
        {
            TokenSet = new TokenSet
            {
                AccessToken = "access-token",
                RefreshToken = "refresh-token",
                Issuer = "https://example.com",
                Sub = sub
            }
        };

        await store.StoreAsync(sub, data);
        var retrieved = await store.GetAsync(sub);

        Assert.NotNull(retrieved);
        Assert.Equal(data.TokenSet.AccessToken, retrieved!.TokenSet.AccessToken);
    }

    [Fact]
    public async Task MemorySessionStore_Delete_RemovesSession()
    {
        var store = new MemoryOAuthSessionStore();
        var sub = "did:plc:test123";
        var data = new OAuthSessionData
        {
            TokenSet = new TokenSet { Sub = sub }
        };

        await store.StoreAsync(sub, data);
        await store.DeleteAsync(sub);
        var retrieved = await store.GetAsync(sub);

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task MemorySessionStore_NotFound_ReturnsNull()
    {
        var store = new MemoryOAuthSessionStore();
        var retrieved = await store.GetAsync("did:plc:nonexistent");

        Assert.Null(retrieved);
    }
}

public class DPoPNonceCacheTests
{
    [Fact]
    public void GetOrigin_ExtractsOriginCorrectly()
    {
        var origin = DPoPNonceCache.GetOrigin("https://example.com/path?query=value");
        Assert.Equal("https://example.com", origin);
    }

    [Fact]
    public void GetOrigin_IncludesPort()
    {
        var origin = DPoPNonceCache.GetOrigin("https://example.com:8080/path");
        Assert.Equal("https://example.com:8080", origin);
    }

    [Fact]
    public void Set_And_Get_Works()
    {
        var cache = new DPoPNonceCache();

        cache.Set("https://example.com/token", "nonce-123");
        var retrieved = cache.Get("https://example.com/other-path");

        Assert.Equal("nonce-123", retrieved);
    }

    [Fact]
    public void Get_DifferentOrigin_ReturnsNull()
    {
        var cache = new DPoPNonceCache();

        cache.Set("https://example.com/token", "nonce-123");
        var retrieved = cache.Get("https://other.com/path");

        Assert.Null(retrieved);
    }

    [Fact]
    public void Clear_RemovesAllNonces()
    {
        var cache = new DPoPNonceCache();

        cache.Set("https://example.com/token", "nonce-123");
        cache.Clear();
        var retrieved = cache.Get("https://example.com/other");

        Assert.Null(retrieved);
    }
}

public class TokenSetTests
{
    [Fact]
    public void FromResponse_CreatesValidTokenSet()
    {
        var response = new OAuthTokenResponse
        {
            AccessToken = "access-token",
            RefreshToken = "refresh-token",
            ExpiresIn = 3600,
            Scope = "atproto",
            Sub = "did:plc:test"
        };

        var tokenSet = TokenSet.FromResponse(response, "https://issuer.com", "https://pds.com");

        Assert.Equal("access-token", tokenSet.AccessToken);
        Assert.Equal("refresh-token", tokenSet.RefreshToken);
        Assert.Equal("https://issuer.com", tokenSet.Issuer);
        Assert.Equal("https://pds.com", tokenSet.Audience);
        Assert.Equal("did:plc:test", tokenSet.Sub);
        Assert.NotNull(tokenSet.ExpiresAt);
    }

    [Fact]
    public void IsExpired_NotExpired_ReturnsFalse()
    {
        var tokenSet = new TokenSet
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        Assert.False(tokenSet.IsExpired());
    }

    [Fact]
    public void IsExpired_Expired_ReturnsTrue()
    {
        var tokenSet = new TokenSet
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        };

        Assert.True(tokenSet.IsExpired());
    }

    [Fact]
    public void IsExpired_WithBuffer_ExpiresEarly()
    {
        var tokenSet = new TokenSet
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(10)
        };

        // Should be considered expired when buffer is 30 seconds
        Assert.True(tokenSet.IsExpired(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void IsExpired_NoExpiry_ReturnsFalse()
    {
        var tokenSet = new TokenSet
        {
            ExpiresAt = null
        };

        Assert.False(tokenSet.IsExpired());
    }
}
