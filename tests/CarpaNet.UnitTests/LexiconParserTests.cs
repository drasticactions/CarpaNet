using CarpaNet.Parsing;
using Xunit;

namespace CarpaNet.UnitTests;

public class LexiconParserTests
{
    [Fact]
    public void Parse_ValidLexicon_ReturnsDocument()
    {
        var json = """
        {
            "lexicon": 1,
            "id": "app.bsky.feed.post",
            "description": "A post record",
            "defs": {
                "main": {
                    "type": "record",
                    "record": {
                        "type": "object",
                        "properties": {
                            "text": {
                                "type": "string",
                                "maxLength": 3000
                            }
                        },
                        "required": ["text"]
                    }
                }
            }
        }
        """;

        var doc = LexiconParser.Parse(json);

        Assert.NotNull(doc);
        Assert.Equal(1, doc.Lexicon);
        Assert.Equal("app.bsky.feed.post", doc.Id);
        Assert.Equal("A post record", doc.Description);
        Assert.Contains("main", doc.Defs.Keys);
        Assert.Equal("record", doc.Defs["main"].Type);
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(LexiconParser.Parse(null));
        Assert.Null(LexiconParser.Parse(""));
        Assert.Null(LexiconParser.Parse("   "));
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        var invalidJson = "{ invalid json }";
        Assert.Null(LexiconParser.Parse(invalidJson));
    }

    [Fact]
    public void Parse_ObjectDefinition_ParsesProperties()
    {
        var json = """
        {
            "lexicon": 1,
            "id": "test.defs",
            "defs": {
                "viewRecord": {
                    "type": "object",
                    "properties": {
                        "uri": {
                            "type": "string",
                            "format": "at-uri"
                        },
                        "value": {
                            "type": "unknown"
                        }
                    },
                    "required": ["uri", "value"]
                }
            }
        }
        """;

        var doc = LexiconParser.Parse(json);

        Assert.NotNull(doc);
        var def = doc.Defs["viewRecord"];
        Assert.Equal("object", def.Type);
        Assert.NotNull(def.Properties);
        Assert.Equal(2, def.Properties.Count);
        Assert.Equal("at-uri", def.Properties["uri"].Format);
        Assert.Contains("uri", def.Required!);
    }

    [Fact]
    public void Parse_UnionDefinition_ParsesRefs()
    {
        var json = """
        {
            "lexicon": 1,
            "id": "test.union",
            "defs": {
                "embedUnion": {
                    "type": "union",
                    "refs": [
                        "app.bsky.embed.images",
                        "app.bsky.embed.video",
                        "app.bsky.embed.external"
                    ]
                }
            }
        }
        """;

        var doc = LexiconParser.Parse(json);

        Assert.NotNull(doc);
        var def = doc.Defs["embedUnion"];
        Assert.Equal("union", def.Type);
        Assert.NotNull(def.Refs);
        Assert.Equal(3, def.Refs.Count);
        Assert.Contains("app.bsky.embed.images", def.Refs);
    }

    [Fact]
    public void Parse_QueryDefinition_ParsesParametersAndOutput()
    {
        var json = """
        {
            "lexicon": 1,
            "id": "app.bsky.feed.getTimeline",
            "defs": {
                "main": {
                    "type": "query",
                    "parameters": {
                        "type": "params",
                        "properties": {
                            "limit": {
                                "type": "integer",
                                "minimum": 1,
                                "maximum": 100,
                                "default": 50
                            }
                        }
                    },
                    "output": {
                        "encoding": "application/json",
                        "schema": {
                            "type": "object",
                            "properties": {
                                "cursor": {
                                    "type": "string"
                                }
                            }
                        }
                    }
                }
            }
        }
        """;

        var doc = LexiconParser.Parse(json);

        Assert.NotNull(doc);
        var def = doc.Defs["main"];
        Assert.Equal("query", def.Type);
        Assert.NotNull(def.Parameters);
        Assert.NotNull(def.Parameters.Properties);
        Assert.Equal(1, def.Parameters.Properties["limit"].Minimum);
        Assert.Equal(100, def.Parameters.Properties["limit"].Maximum);
        Assert.NotNull(def.Output);
        Assert.Equal("application/json", def.Output.Encoding);
    }

    [Fact]
    public void Parse_ArrayDefinition_ParsesItems()
    {
        var json = """
        {
            "lexicon": 1,
            "id": "test.array",
            "defs": {
                "tags": {
                    "type": "array",
                    "items": {
                        "type": "string",
                        "maxLength": 64
                    },
                    "maxLength": 8
                }
            }
        }
        """;

        var doc = LexiconParser.Parse(json);

        Assert.NotNull(doc);
        var def = doc.Defs["tags"];
        Assert.Equal("array", def.Type);
        Assert.NotNull(def.Items);
        Assert.Equal("string", def.Items.Type);
        Assert.Equal(64, def.Items.MaxLength);
        Assert.Equal(8, def.MaxLength);
    }

    [Fact]
    public void TryParse_ValidJson_ReturnsTrue()
    {
        var json = """
        {
            "lexicon": 1,
            "id": "test.type",
            "defs": {}
        }
        """;

        var result = LexiconParser.TryParse(json, out var doc, out var error);

        Assert.True(result);
        Assert.NotNull(doc);
        Assert.Null(error);
    }

    [Fact]
    public void TryParse_InvalidJson_ReturnsFalse()
    {
        var json = "{ invalid }";

        var result = LexiconParser.TryParse(json, out var doc, out var error);

        Assert.False(result);
        Assert.Null(doc);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_EmptyJson_ReturnsFalse()
    {
        var result = LexiconParser.TryParse("", out var doc, out var error);

        Assert.False(result);
        Assert.Null(doc);
        Assert.Equal("Empty or null JSON content", error);
    }

    [Fact]
    public void Parse_SupportsTrailingCommas()
    {
        var json = """
        {
            "lexicon": 1,
            "id": "test.type",
            "defs": {
                "main": {
                    "type": "object",
                },
            },
        }
        """;

        var doc = LexiconParser.Parse(json);
        Assert.NotNull(doc);
    }
}
