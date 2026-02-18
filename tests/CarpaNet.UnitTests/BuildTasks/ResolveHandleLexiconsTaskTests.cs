using System;
using System.Collections.Generic;
using System.IO;
using CarpaNet.BuildTasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

namespace CarpaNet.UnitTests.BuildTasks;

public class ResolveHandleLexiconsTaskTests : IDisposable
{
    private readonly string _tempDir;

    public ResolveHandleLexiconsTaskTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "carpanet-handle-task-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Execute_WithNoHandles_ReturnsTrue()
    {
        var task = new ResolveHandleLexiconsTask
        {
            Handles = Array.Empty<ITaskItem>(),
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Empty(task.ResolvedLexiconFiles);
    }

    [Fact]
    public void Execute_WithInvalidHandle_SingleSegment_ReturnsFalse()
    {
        var task = new ResolveHandleLexiconsTask
        {
            Handles = [new TaskItem("noDotsHere")],
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.False(result);
    }

    [Fact]
    public void Execute_WithInvalidHandle_DigitStartingTld_ReturnsFalse()
    {
        var task = new ResolveHandleLexiconsTask
        {
            Handles = [new TaskItem("user.123invalid")],
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.False(result);
    }

    [Fact]
    public void Execute_StripsAtPrefix()
    {
        // @handle.social → handle.social, which is valid but will fail on network
        var task = new ResolveHandleLexiconsTask
        {
            Handles = [new TaskItem("@handle.social")],
            CacheDir = _tempDir,
            FailOnError = false,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        // Should not fail with "invalid handle" — gets past validation
        // Will fail on network resolution but with FailOnError=false that's just a warning
        Assert.True(result);
    }

    [Fact]
    public void Execute_UsesCachedManifest_WhenAvailable()
    {
        var cache = new LexiconCache(_tempDir);
        var nsids = new List<string>
        {
            "com.whtwnd.blog.entry",
            "com.whtwnd.blog.comment",
        };

        foreach (var nsid in nsids)
        {
            cache.Store(nsid, $$$"""{"lexicon":1,"id":"{{{nsid}}}"}""");
        }

        cache.StoreAuthorityManifest("whtwnd.com", nsids);

        var task = new ResolveHandleLexiconsTask
        {
            Handles = [new TaskItem("whtwnd.com")],
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Equal(2, task.ResolvedLexiconFiles.Length);
        Assert.Equal(cache.GetJsonPath("com.whtwnd.blog.entry"), task.ResolvedLexiconFiles[0].ItemSpec);
        Assert.Equal(cache.GetJsonPath("com.whtwnd.blog.comment"), task.ResolvedLexiconFiles[1].ItemSpec);
    }

    [Fact]
    public void Execute_ReResolvesWhenIndividualNsidCacheMissing()
    {
        var cache = new LexiconCache(_tempDir);
        var nsids = new List<string> { "com.whtwnd.blog.entry" };
        cache.StoreAuthorityManifest("whtwnd.com", nsids);

        // Don't store the individual NSID — simulates missing cache entry
        var task = new ResolveHandleLexiconsTask
        {
            Handles = [new TaskItem("whtwnd.com")],
            CacheDir = _tempDir,
            FailOnError = false,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        // Should succeed but with a warning (FailOnError=false)
        Assert.True(result);
    }

    [Fact]
    public void Execute_CachedManifest_WithEmptyNsidList_ReturnsEmpty()
    {
        var cache = new LexiconCache(_tempDir);
        cache.StoreAuthorityManifest("empty.social", new List<string>());

        var task = new ResolveHandleLexiconsTask
        {
            Handles = [new TaskItem("empty.social")],
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Empty(task.ResolvedLexiconFiles);
    }

    [Theory]
    [InlineData("bsky-lexicons.bsky.social", true)]
    [InlineData("whtwnd.com", true)]
    [InlineData("user.bsky.social", true)]
    [InlineData("my-handle.example.com", true)]
    [InlineData("single", false)]
    [InlineData("user.123", false)]
    [InlineData("", false)]
    [InlineData(".leading.dot", false)]
    [InlineData("trailing.dot.", false)]
    [InlineData("has space.com", false)]
    public void IsValidHandle_ValidatesCorrectly(string handle, bool expected)
    {
        Assert.Equal(expected, ResolveHandleLexiconsTask.IsValidHandle(handle));
    }

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
