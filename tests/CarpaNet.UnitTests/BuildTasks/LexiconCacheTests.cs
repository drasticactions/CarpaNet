using System;
using System.Globalization;
using System.IO;
using CarpaNet.BuildTasks;
using Xunit;

namespace CarpaNet.UnitTests.BuildTasks;

public class LexiconCacheTests : IDisposable
{
    private readonly string _tempDir;

    public LexiconCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "carpanet-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Store_And_TryGet_RoundTrips()
    {
        var cache = new LexiconCache(_tempDir);
        var nsid = "com.example.myapp.getProfile";
        var json = """{"lexicon":1,"id":"com.example.myapp.getProfile"}""";

        cache.Store(nsid, json);
        var result = cache.TryGet(nsid);

        Assert.Equal(json, result);
    }

    [Fact]
    public void TryGet_ReturnsNull_WhenNotCached()
    {
        var cache = new LexiconCache(_tempDir);
        var result = cache.TryGet("com.example.nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void TryGet_ReturnsNull_WhenExpired()
    {
        var cache = new LexiconCache(_tempDir, ttlHours: 1);
        var nsid = "com.example.myapp.getProfile";
        var json = """{"lexicon":1}""";

        // Write files manually with old timestamp
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(cache.GetJsonPath(nsid), json);
        var oldTime = DateTimeOffset.UtcNow.AddHours(-2);
        File.WriteAllText(cache.GetMetaPath(nsid), oldTime.ToString("O", CultureInfo.InvariantCulture));

        var result = cache.TryGet(nsid);

        Assert.Null(result);
    }

    [Fact]
    public void TryGet_ReturnsCached_WhenNotExpired()
    {
        var cache = new LexiconCache(_tempDir, ttlHours: 24);
        var nsid = "com.example.myapp.getProfile";
        var json = """{"lexicon":1}""";

        cache.Store(nsid, json);
        var result = cache.TryGet(nsid);

        Assert.Equal(json, result);
    }

    [Fact]
    public void IsCached_ReturnsTrue_WhenValid()
    {
        var cache = new LexiconCache(_tempDir);
        cache.Store("com.example.test", """{"lexicon":1}""");

        Assert.True(cache.IsCached("com.example.test"));
    }

    [Fact]
    public void IsCached_ReturnsFalse_WhenMissing()
    {
        var cache = new LexiconCache(_tempDir);
        Assert.False(cache.IsCached("com.example.missing"));
    }

    [Fact]
    public void Store_CreatesDirectory_IfNotExists()
    {
        var subDir = Path.Combine(_tempDir, "sub", "nested");
        var cache = new LexiconCache(subDir);
        var nsid = "com.example.test";

        cache.Store(nsid, """{"lexicon":1}""");

        Assert.True(File.Exists(cache.GetJsonPath(nsid)));
        Assert.True(File.Exists(cache.GetMetaPath(nsid)));
    }

    [Fact]
    public void GetJsonPath_ReturnsExpectedPath()
    {
        var cache = new LexiconCache(_tempDir);
        var expected = Path.Combine(_tempDir, "com.example.test.json");

        Assert.Equal(expected, cache.GetJsonPath("com.example.test"));
    }

    [Fact]
    public void ZeroTtl_ForcesRefresh()
    {
        var cache = new LexiconCache(_tempDir, ttlHours: 0);
        var nsid = "com.example.test";
        cache.Store(nsid, """{"lexicon":1}""");

        // TTL of 0 means everything is expired
        Assert.Null(cache.TryGet(nsid));
    }

    [Fact]
    public void StoreAuthorityManifest_And_TryGet_RoundTrips()
    {
        var cache = new LexiconCache(_tempDir);
        var authority = "blog.pckt";
        var nsids = new System.Collections.Generic.List<string>
        {
            "blog.pckt.block.text",
            "blog.pckt.block.image",
            "blog.pckt.content",
        };

        cache.StoreAuthorityManifest(authority, nsids);
        var result = cache.TryGetAuthorityManifest(authority);

        Assert.NotNull(result);
        Assert.Equal(nsids, result);
    }

    [Fact]
    public void TryGetAuthorityManifest_ReturnsNull_WhenNotCached()
    {
        var cache = new LexiconCache(_tempDir);
        var result = cache.TryGetAuthorityManifest("com.example.nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void TryGetAuthorityManifest_ReturnsNull_WhenExpired()
    {
        var cache = new LexiconCache(_tempDir, ttlHours: 1);
        var authority = "blog.pckt";

        // Write files manually with old timestamp
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(cache.GetAuthorityJsonPath(authority), """["blog.pckt.content"]""");
        var oldTime = DateTimeOffset.UtcNow.AddHours(-2);
        File.WriteAllText(cache.GetAuthorityMetaPath(authority), oldTime.ToString("O", CultureInfo.InvariantCulture));

        var result = cache.TryGetAuthorityManifest(authority);

        Assert.Null(result);
    }

    [Fact]
    public void TryGetAuthorityManifest_ReturnsCached_WhenNotExpired()
    {
        var cache = new LexiconCache(_tempDir, ttlHours: 24);
        var authority = "blog.pckt";
        var nsids = new System.Collections.Generic.List<string> { "blog.pckt.content" };

        cache.StoreAuthorityManifest(authority, nsids);
        var result = cache.TryGetAuthorityManifest(authority);

        Assert.NotNull(result);
        Assert.Equal(nsids, result);
    }

    [Fact]
    public void GetAuthorityJsonPath_ReturnsExpectedPath()
    {
        var cache = new LexiconCache(_tempDir);
        var expected = Path.Combine(_tempDir, "_authority.blog.pckt.json");

        Assert.Equal(expected, cache.GetAuthorityJsonPath("blog.pckt"));
    }

    [Fact]
    public void GetAuthorityMetaPath_ReturnsExpectedPath()
    {
        var cache = new LexiconCache(_tempDir);
        var expected = Path.Combine(_tempDir, "_authority.blog.pckt.meta");

        Assert.Equal(expected, cache.GetAuthorityMetaPath("blog.pckt"));
    }

    [Fact]
    public void StoreAuthorityManifest_EmptyList_RoundTrips()
    {
        var cache = new LexiconCache(_tempDir);
        var authority = "com.empty";
        var nsids = new System.Collections.Generic.List<string>();

        cache.StoreAuthorityManifest(authority, nsids);
        var result = cache.TryGetAuthorityManifest(authority);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ZeroTtl_ForcesRefresh_ForAuthorityManifest()
    {
        var cache = new LexiconCache(_tempDir, ttlHours: 0);
        var authority = "blog.pckt";
        cache.StoreAuthorityManifest(authority, new System.Collections.Generic.List<string> { "blog.pckt.content" });

        Assert.Null(cache.TryGetAuthorityManifest(authority));
    }

    [Fact]
    public void AuthorityManifest_WithDid_SanitizesColonsInFilename()
    {
        var cache = new LexiconCache(_tempDir);
        var did = "did:plc:revjuqmkvrw6fnkxppqtszpv";

        // Colons should be replaced with underscores in the filename
        var jsonPath = cache.GetAuthorityJsonPath(did);
        var metaPath = cache.GetAuthorityMetaPath(did);

        Assert.DoesNotContain(":", Path.GetFileName(jsonPath));
        Assert.DoesNotContain(":", Path.GetFileName(metaPath));
        Assert.Contains("did_plc_", Path.GetFileName(jsonPath));
    }

    [Fact]
    public void AuthorityManifest_WithDid_RoundTrips()
    {
        var cache = new LexiconCache(_tempDir);
        var did = "did:plc:revjuqmkvrw6fnkxppqtszpv";
        var nsids = new System.Collections.Generic.List<string>
        {
            "blog.pckt.block.text",
            "blog.pckt.content",
        };

        cache.StoreAuthorityManifest(did, nsids);
        var result = cache.TryGetAuthorityManifest(did);

        Assert.NotNull(result);
        Assert.Equal(nsids, result);
    }
}
