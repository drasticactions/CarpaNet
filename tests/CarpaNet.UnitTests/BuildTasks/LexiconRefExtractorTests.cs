using System;
using System.IO;
using CarpaNet.BuildTasks;
using Xunit;

namespace CarpaNet.UnitTests.BuildTasks;

public class LexiconRefExtractorTests
{
    [Fact]
    public void ExtractReferencedNsids_DirectRef_ReturnsNsid()
    {
        var json = """
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
        """;

        var result = LexiconRefExtractor.ExtractReferencedNsids(json);

        Assert.Single(result);
        Assert.Contains("app.bsky.actor.defs", result);
    }

    [Fact]
    public void ExtractReferencedNsids_UnionRefs_ReturnsAllNsids()
    {
        var json = """
        {
            "lexicon": 1,
            "id": "app.bsky.feed.defs",
            "defs": {
                "feedViewPost": {
                    "type": "object",
                    "properties": {
                        "embed": {
                            "type": "union",
                            "refs": [
                                "app.bsky.embed.images#view",
                                "app.bsky.embed.external#view",
                                "app.bsky.embed.record#view"
                            ]
                        }
                    }
                }
            }
        }
        """;

        var result = LexiconRefExtractor.ExtractReferencedNsids(json);

        Assert.Equal(3, result.Count);
        Assert.Contains("app.bsky.embed.images", result);
        Assert.Contains("app.bsky.embed.external", result);
        Assert.Contains("app.bsky.embed.record", result);
    }

    [Fact]
    public void ExtractReferencedNsids_LocalRef_ExcludedFromResult()
    {
        var json = """
        {
            "lexicon": 1,
            "id": "app.bsky.feed.defs",
            "defs": {
                "main": {
                    "type": "object",
                    "properties": {
                        "item": {
                            "type": "ref",
                            "ref": "#localType"
                        }
                    }
                },
                "localType": {
                    "type": "object"
                }
            }
        }
        """;

        var result = LexiconRefExtractor.ExtractReferencedNsids(json);

        Assert.Empty(result);
    }

    [Fact]
    public void ExtractReferencedNsids_SelfRef_ExcludedFromResult()
    {
        var json = """
        {
            "lexicon": 1,
            "id": "app.bsky.feed.defs",
            "defs": {
                "main": {
                    "type": "object",
                    "properties": {
                        "item": {
                            "type": "ref",
                            "ref": "app.bsky.feed.defs#otherType"
                        }
                    }
                }
            }
        }
        """;

        var result = LexiconRefExtractor.ExtractReferencedNsids(json);

        Assert.Empty(result);
    }

    [Fact]
    public void ExtractReferencedNsids_DeeplyNested_FindsRefs()
    {
        var json = """
        {
            "lexicon": 1,
            "id": "app.bsky.feed.post",
            "defs": {
                "main": {
                    "type": "record",
                    "record": {
                        "type": "object",
                        "properties": {
                            "facets": {
                                "type": "array",
                                "items": {
                                    "type": "ref",
                                    "ref": "app.bsky.richtext.facet"
                                }
                            }
                        }
                    }
                }
            }
        }
        """;

        var result = LexiconRefExtractor.ExtractReferencedNsids(json);

        Assert.Single(result);
        Assert.Contains("app.bsky.richtext.facet", result);
    }

    [Fact]
    public void ExtractReferencedNsids_Deduplication_ReturnsSingleEntry()
    {
        var json = """
        {
            "lexicon": 1,
            "id": "app.bsky.feed.defs",
            "defs": {
                "type1": {
                    "type": "object",
                    "properties": {
                        "a": { "type": "ref", "ref": "app.bsky.actor.defs#profileView" }
                    }
                },
                "type2": {
                    "type": "object",
                    "properties": {
                        "b": { "type": "ref", "ref": "app.bsky.actor.defs#profileViewBasic" }
                    }
                }
            }
        }
        """;

        var result = LexiconRefExtractor.ExtractReferencedNsids(json);

        Assert.Single(result);
        Assert.Contains("app.bsky.actor.defs", result);
    }

    [Fact]
    public void ExtractNsid_ReadsIdField()
    {
        var json = """{"lexicon":1,"id":"com.example.myapp.getProfile","defs":{}}""";

        var nsid = LexiconRefExtractor.ExtractNsid(json);

        Assert.Equal("com.example.myapp.getProfile", nsid);
    }

    [Fact]
    public void ExtractNsid_MissingId_ReturnsNull()
    {
        var json = """{"lexicon":1,"defs":{}}""";

        var nsid = LexiconRefExtractor.ExtractNsid(json);

        Assert.Null(nsid);
    }

    [Fact]
    public void ExtractReferencedNsidsFromFile_ReadsFromDisk()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "lexicon": 1,
                "id": "app.bsky.feed.post",
                "defs": {
                    "main": {
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
            """);

            var result = LexiconRefExtractor.ExtractReferencedNsidsFromFile(tempFile);

            Assert.Single(result);
            Assert.Contains("app.bsky.actor.defs", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
