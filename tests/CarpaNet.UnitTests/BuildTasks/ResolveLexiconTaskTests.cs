using System;
using System.IO;
using CarpaNet.BuildTasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

namespace CarpaNet.UnitTests.BuildTasks;

public class ResolveLexiconTaskTests : IDisposable
{
    private readonly string _tempDir;

    public ResolveLexiconTaskTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "carpanet-task-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Execute_WithNoNsids_ReturnsTrue()
    {
        var task = new ResolveLexiconTask
        {
            Nsids = Array.Empty<ITaskItem>(),
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Empty(task.ResolvedLexiconFiles);
    }

    [Fact]
    public void Execute_WithInvalidNsid_ReturnsFalse()
    {
        var task = new ResolveLexiconTask
        {
            Nsids = [new TaskItem("invalid")],
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.False(result);
    }

    [Fact]
    public void Execute_UsesCachedLexicon_WhenAvailable()
    {
        // Pre-populate the cache
        var cache = new LexiconCache(_tempDir);
        var nsid = "com.example.myapp.getProfile";
        var json = """{"lexicon":1,"id":"com.example.myapp.getProfile"}""";
        cache.Store(nsid, json);

        var task = new ResolveLexiconTask
        {
            Nsids = [new TaskItem(nsid)],
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Single(task.ResolvedLexiconFiles);
        Assert.Equal(cache.GetJsonPath(nsid), task.ResolvedLexiconFiles[0].ItemSpec);
    }

    [Fact]
    public void Execute_WithInvalidNsid_StillReportsError_WhenFailOnErrorFalse()
    {
        // Invalid NSID always fails regardless of FailOnError setting
        var task = new ResolveLexiconTask
        {
            Nsids = [new TaskItem("invalid")],
            CacheDir = _tempDir,
            FailOnError = false,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.False(result);
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
