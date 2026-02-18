using System;
using System.Collections.Generic;
using System.IO;
using CarpaNet.BuildTasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

namespace CarpaNet.UnitTests.BuildTasks;

public class ResolveAuthorityLexiconsTaskTests : IDisposable
{
    private readonly string _tempDir;

    public ResolveAuthorityLexiconsTaskTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "carpanet-authority-task-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Execute_WithNoAuthorities_ReturnsTrue()
    {
        var task = new ResolveAuthorityLexiconsTask
        {
            Authorities = Array.Empty<ITaskItem>(),
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Empty(task.ResolvedLexiconFiles);
    }

    [Fact]
    public void Execute_WithInvalidAuthority_ReturnsFalse()
    {
        var task = new ResolveAuthorityLexiconsTask
        {
            Authorities = [new TaskItem("invalid")],
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.False(result);
    }

    [Fact]
    public void Execute_WithInvalidAuthority_SingleSegment_ReturnsFalse()
    {
        var task = new ResolveAuthorityLexiconsTask
        {
            Authorities = [new TaskItem("noDotsHere")],
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.False(result);
    }

    [Fact]
    public void Execute_UsesCachedManifest_WhenAvailable()
    {
        // Pre-populate the cache with authority manifest and individual NSIDs
        var cache = new LexiconCache(_tempDir);
        var nsids = new List<string>
        {
            "blog.pckt.block.text",
            "blog.pckt.block.image",
        };

        foreach (var nsid in nsids)
        {
            cache.Store(nsid, $$$"""{"lexicon":1,"id":"{{{nsid}}}"}""");
        }

        cache.StoreAuthorityManifest("blog.pckt", nsids);

        var task = new ResolveAuthorityLexiconsTask
        {
            Authorities = [new TaskItem("blog.pckt")],
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Equal(2, task.ResolvedLexiconFiles.Length);
        Assert.Equal(cache.GetJsonPath("blog.pckt.block.text"), task.ResolvedLexiconFiles[0].ItemSpec);
        Assert.Equal(cache.GetJsonPath("blog.pckt.block.image"), task.ResolvedLexiconFiles[1].ItemSpec);
    }

    [Fact]
    public void Execute_UsesCachedManifest_WithMultipleAuthorities()
    {
        var cache = new LexiconCache(_tempDir);

        // First authority
        var nsids1 = new List<string> { "blog.pckt.content" };
        cache.Store("blog.pckt.content", """{"lexicon":1,"id":"blog.pckt.content"}""");
        cache.StoreAuthorityManifest("blog.pckt", nsids1);

        // Second authority
        var nsids2 = new List<string> { "site.standard.publication" };
        cache.Store("site.standard.publication", """{"lexicon":1,"id":"site.standard.publication"}""");
        cache.StoreAuthorityManifest("site.standard", nsids2);

        var task = new ResolveAuthorityLexiconsTask
        {
            Authorities = [new TaskItem("blog.pckt"), new TaskItem("site.standard")],
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Equal(2, task.ResolvedLexiconFiles.Length);
    }

    [Fact]
    public void Execute_ReResolvesWhenIndividualNsidCacheMissing()
    {
        // Store authority manifest but don't store individual NSIDs
        var cache = new LexiconCache(_tempDir);
        var nsids = new List<string> { "blog.pckt.block.text" };
        cache.StoreAuthorityManifest("blog.pckt", nsids);

        // Don't store the individual NSID — this simulates a missing cache entry
        // The task should try to re-resolve from network, which will fail
        // since there's no DNS record for blog.pckt in test environment
        var task = new ResolveAuthorityLexiconsTask
        {
            Authorities = [new TaskItem("blog.pckt")],
            CacheDir = _tempDir,
            FailOnError = false,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        // Should succeed but with a warning (FailOnError=false)
        Assert.True(result);
    }

    [Fact]
    public void Execute_WithInvalidAuthority_StillReportsError_WhenFailOnErrorFalse()
    {
        // Invalid authority always fails regardless of FailOnError setting
        var task = new ResolveAuthorityLexiconsTask
        {
            Authorities = [new TaskItem("invalid")],
            CacheDir = _tempDir,
            FailOnError = false,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.False(result);
    }

    [Fact]
    public void Execute_CachedManifest_WithEmptyNsidList_ReturnsEmpty()
    {
        var cache = new LexiconCache(_tempDir);
        cache.StoreAuthorityManifest("com.empty", new List<string>());

        var task = new ResolveAuthorityLexiconsTask
        {
            Authorities = [new TaskItem("com.empty")],
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Empty(task.ResolvedLexiconFiles);
    }

    /// <summary>
    /// Minimal IBuildEngine implementation for testing MSBuild tasks.
    /// </summary>
    private sealed class FakeBuildEngine : IBuildEngine
    {
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;

        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => true;
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e) { }
    }
}
