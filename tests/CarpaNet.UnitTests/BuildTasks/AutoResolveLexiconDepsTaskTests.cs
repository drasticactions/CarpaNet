using System;
using System.IO;
using CarpaNet.BuildTasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

namespace CarpaNet.UnitTests.BuildTasks;

public class AutoResolveLexiconDepsTaskTests : IDisposable
{
    private readonly string _tempDir;

    public AutoResolveLexiconDepsTaskTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "carpanet-autoresolve-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Execute_WithNoFiles_ReturnsTrue()
    {
        var task = new AutoResolveLexiconDepsTask
        {
            LexiconFiles = Array.Empty<ITaskItem>(),
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Empty(task.AdditionalResolvedFiles);
    }

    [Fact]
    public void Execute_FilesWithNoExternalRefs_ReturnsEmpty()
    {
        // Create a lexicon file with no external refs
        var lexiconPath = Path.Combine(_tempDir, "test.json");
        File.WriteAllText(lexiconPath, """
        {
            "lexicon": 1,
            "id": "com.example.myapp.getProfile",
            "defs": {
                "main": {
                    "type": "query",
                    "output": {
                        "encoding": "application/json",
                        "schema": {
                            "type": "object",
                            "properties": {
                                "name": { "type": "string" }
                            }
                        }
                    }
                }
            }
        }
        """);

        var task = new AutoResolveLexiconDepsTask
        {
            LexiconFiles = new ITaskItem[] { new TaskItem(lexiconPath) },
            CacheDir = _tempDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Empty(task.AdditionalResolvedFiles);
    }

    [Fact]
    public void Execute_PreCachedTransitiveDep_UsesCacheWithoutResolver()
    {
        // Create a lexicon that references another NSID
        var lexiconPath = Path.Combine(_tempDir, "post.json");
        File.WriteAllText(lexiconPath, """
        {
            "lexicon": 1,
            "id": "app.bsky.feed.post",
            "defs": {
                "main": {
                    "type": "record",
                    "record": {
                        "type": "object",
                        "properties": {
                            "author": {
                                "type": "ref",
                                "ref": "app.bsky.actor.defs#profileView"
                            }
                        }
                    }
                }
            }
        }
        """);

        // Pre-populate the cache with the dependency
        var cacheDir = Path.Combine(_tempDir, "cache");
        var cache = new LexiconCache(cacheDir);
        var depJson = """{"lexicon":1,"id":"app.bsky.actor.defs","defs":{"profileView":{"type":"object","properties":{"name":{"type":"string"}}}}}""";
        cache.Store("app.bsky.actor.defs", depJson);

        var task = new AutoResolveLexiconDepsTask
        {
            LexiconFiles = new ITaskItem[] { new TaskItem(lexiconPath) },
            CacheDir = cacheDir,
            BuildEngine = new FakeBuildEngine(),
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Single(task.AdditionalResolvedFiles);
        Assert.Equal(cache.GetJsonPath("app.bsky.actor.defs"), task.AdditionalResolvedFiles[0].ItemSpec);
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
