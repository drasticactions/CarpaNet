using System;
using System.Threading.Tasks;
using CarpaNet.Identity;
using Xunit;

namespace CarpaNet.UnitTests.Identity;

public class MemoryIdentityCacheTests
{
    [Fact]
    public async Task SetAndGetDidDocument_ReturnsStoredDocument()
    {
        var cache = new MemoryIdentityCache();
        var did = "did:plc:abc123";
        var document = new DidDocument { Id = did };

        await cache.SetDidDocumentAsync(did, document);
        var result = await cache.GetDidDocumentAsync(did);

        Assert.NotNull(result);
        Assert.Equal(did, result!.Id);
    }

    [Fact]
    public async Task GetDidDocument_NonExistent_ReturnsNull()
    {
        var cache = new MemoryIdentityCache();

        var result = await cache.GetDidDocumentAsync("did:plc:nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAndGetHandleDid_ReturnsStoredDid()
    {
        var cache = new MemoryIdentityCache();
        var handle = "alice.bsky.social";
        var did = "did:plc:abc123";

        await cache.SetHandleDidAsync(handle, did);
        var result = await cache.GetHandleDidAsync(handle);

        Assert.Equal(did, result);
    }

    [Fact]
    public async Task GetHandleDid_NonExistent_ReturnsNull()
    {
        var cache = new MemoryIdentityCache();

        var result = await cache.GetHandleDidAsync("nonexistent.handle");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDidDocument_CaseInsensitive()
    {
        var cache = new MemoryIdentityCache();
        var did = "did:plc:ABC123";
        var document = new DidDocument { Id = did };

        await cache.SetDidDocumentAsync(did, document);
        var result = await cache.GetDidDocumentAsync("did:plc:abc123");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetHandleDid_CaseInsensitive()
    {
        var cache = new MemoryIdentityCache();
        var handle = "Alice.Bsky.Social";
        var did = "did:plc:abc123";

        await cache.SetHandleDidAsync(handle, did);
        var result = await cache.GetHandleDidAsync("alice.bsky.social");

        Assert.Equal(did, result);
    }

    [Fact]
    public async Task GetDidDocument_Expired_ReturnsNull()
    {
        // Create cache with very short TTL
        var cache = new MemoryIdentityCache(
            didDocumentTtl: TimeSpan.FromMilliseconds(1),
            handleTtl: TimeSpan.FromMinutes(1));

        var did = "did:plc:abc123";
        var document = new DidDocument { Id = did };

        await cache.SetDidDocumentAsync(did, document);

        // Wait for expiration
        await Task.Delay(10);

        var result = await cache.GetDidDocumentAsync(did);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetHandleDid_Expired_ReturnsNull()
    {
        // Create cache with very short TTL
        var cache = new MemoryIdentityCache(
            didDocumentTtl: TimeSpan.FromMinutes(1),
            handleTtl: TimeSpan.FromMilliseconds(1));

        var handle = "alice.bsky.social";
        var did = "did:plc:abc123";

        await cache.SetHandleDidAsync(handle, did);

        // Wait for expiration
        await Task.Delay(10);

        var result = await cache.GetHandleDidAsync(handle);

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveDidDocument_RemovesEntry()
    {
        var cache = new MemoryIdentityCache();
        var did = "did:plc:abc123";
        var document = new DidDocument { Id = did };

        await cache.SetDidDocumentAsync(did, document);
        cache.RemoveDidDocument(did);
        var result = await cache.GetDidDocumentAsync(did);

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveHandle_RemovesEntry()
    {
        var cache = new MemoryIdentityCache();
        var handle = "alice.bsky.social";
        var did = "did:plc:abc123";

        await cache.SetHandleDidAsync(handle, did);
        cache.RemoveHandle(handle);
        var result = await cache.GetHandleDidAsync(handle);

        Assert.Null(result);
    }

    [Fact]
    public async Task Clear_RemovesAllEntries()
    {
        var cache = new MemoryIdentityCache();

        await cache.SetDidDocumentAsync("did:plc:abc123", new DidDocument { Id = "did:plc:abc123" });
        await cache.SetHandleDidAsync("alice.bsky.social", "did:plc:abc123");

        cache.Clear();

        Assert.Null(await cache.GetDidDocumentAsync("did:plc:abc123"));
        Assert.Null(await cache.GetHandleDidAsync("alice.bsky.social"));
    }

    [Fact]
    public void Constructor_InvalidTtl_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new MemoryIdentityCache(TimeSpan.Zero, TimeSpan.FromMinutes(1)));

        Assert.Throws<ArgumentException>(() =>
            new MemoryIdentityCache(TimeSpan.FromMinutes(1), TimeSpan.Zero));

        Assert.Throws<ArgumentException>(() =>
            new MemoryIdentityCache(TimeSpan.FromMinutes(-1), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Constructor_InvalidMaxEntries_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new MemoryIdentityCache(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), 0));

        Assert.Throws<ArgumentException>(() =>
            new MemoryIdentityCache(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), -1));
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var cache = new MemoryIdentityCache();

        Assert.Equal(TimeSpan.FromMinutes(5), cache.DidDocumentTtl);
        Assert.Equal(TimeSpan.FromMinutes(2), cache.HandleTtl);
        Assert.Equal(1000, cache.MaxEntries);
    }

    [Fact]
    public async Task Count_TracksEntries()
    {
        var cache = new MemoryIdentityCache();

        Assert.Equal(0, cache.DidDocumentCount);
        Assert.Equal(0, cache.HandleCount);

        await cache.SetDidDocumentAsync("did:plc:1", new DidDocument { Id = "did:plc:1" });
        await cache.SetDidDocumentAsync("did:plc:2", new DidDocument { Id = "did:plc:2" });
        await cache.SetHandleDidAsync("alice.bsky.social", "did:plc:1");

        Assert.Equal(2, cache.DidDocumentCount);
        Assert.Equal(1, cache.HandleCount);
    }

    [Fact]
    public async Task SetDidDocument_OverwritesExisting()
    {
        var cache = new MemoryIdentityCache();
        var did = "did:plc:abc123";

        await cache.SetDidDocumentAsync(did, new DidDocument
        {
            Id = did,
            AlsoKnownAs = new System.Collections.Generic.List<string> { "at://old.handle" }
        });
        await cache.SetDidDocumentAsync(did, new DidDocument
        {
            Id = did,
            AlsoKnownAs = new System.Collections.Generic.List<string> { "at://new.handle" }
        });

        var result = await cache.GetDidDocumentAsync(did);

        Assert.NotNull(result);
        Assert.Equal("new.handle", result!.Handle);
    }

    [Fact]
    public async Task EvictExpired_RemovesExpiredEntries()
    {
        // Create cache with very short TTL
        var cache = new MemoryIdentityCache(
            didDocumentTtl: TimeSpan.FromMilliseconds(1),
            handleTtl: TimeSpan.FromMilliseconds(1));

        await cache.SetDidDocumentAsync("did:plc:1", new DidDocument { Id = "did:plc:1" });
        await cache.SetHandleDidAsync("alice.bsky.social", "did:plc:1");

        // Wait for expiration
        await Task.Delay(10);

        cache.EvictExpired();

        Assert.Equal(0, cache.DidDocumentCount);
        Assert.Equal(0, cache.HandleCount);
    }
}

public class IdentityResolverCacheTests
{
    [Fact]
    public void CreateWithCache_CreatesResolverWithCache()
    {
        var resolver = IdentityResolver.CreateWithCache();

        Assert.NotNull(resolver.Cache);
        Assert.IsType<MemoryIdentityCache>(resolver.Cache);

        resolver.Dispose();
    }

    [Fact]
    public void CreateWithCache_CustomCache_UsesProvidedCache()
    {
        var cache = new MemoryIdentityCache(
            didDocumentTtl: TimeSpan.FromMinutes(10),
            handleTtl: TimeSpan.FromMinutes(5));

        var resolver = IdentityResolver.CreateWithCache(cache);

        Assert.Same(cache, resolver.Cache);

        resolver.Dispose();
    }

    [Fact]
    public void DefaultConstructor_NoCache()
    {
        var resolver = new IdentityResolver();

        Assert.Null(resolver.Cache);

        resolver.Dispose();
    }
}
