using CarpaNet.Models;
using Xunit;

namespace CarpaNet.UnitTests;

public class TypeRegistryTests
{
    [Fact]
    public void RegisterDocument_RegistersMainDefinition()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "app.bsky.feed.post",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition { Type = "record" }
            }
        };

        registry.RegisterDocument(doc);

        var typeInfo = registry.Lookup("app.bsky.feed.post");
        Assert.NotNull(typeInfo);
        Assert.Equal("app.bsky.feed.post", typeInfo.FullRef);
        Assert.Equal(LexiconTypeKind.Record, typeInfo.Kind);
        Assert.Equal("Post", typeInfo.CSharpTypeName);
        Assert.Equal("AppBsky.Feed", typeInfo.CSharpNamespace);
    }

    [Fact]
    public void RegisterDocument_RegistersNonMainDefinition()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "app.bsky.feed.defs",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition { Type = "object" },
                ["feedViewPost"] = new LexiconDefinition { Type = "object" }
            }
        };

        registry.RegisterDocument(doc);

        var typeInfo = registry.Lookup("app.bsky.feed.defs#feedViewPost");
        Assert.NotNull(typeInfo);
        Assert.Equal("app.bsky.feed.defs#feedViewPost", typeInfo.FullRef);
        Assert.Equal("DefsFeedViewPost", typeInfo.CSharpTypeName);
    }

    [Fact]
    public void RegisterDocument_RegistersMainUnderMultipleKeys()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "app.bsky.feed.post",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition { Type = "record" }
            }
        };

        registry.RegisterDocument(doc);

        // Should be accessible via multiple keys
        Assert.NotNull(registry.Lookup("app.bsky.feed.post"));
        Assert.NotNull(registry.Lookup("app.bsky.feed.post#main"));
    }

    [Fact]
    public void Lookup_ReturnsNullForUnknownRef()
    {
        var registry = new TypeRegistry();
        var result = registry.Lookup("unknown.type.ref");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("string", LexiconTypeKind.String)]
    [InlineData("boolean", LexiconTypeKind.Boolean)]
    [InlineData("integer", LexiconTypeKind.Integer)]
    [InlineData("bytes", LexiconTypeKind.Bytes)]
    [InlineData("cid-link", LexiconTypeKind.CidLink)]
    [InlineData("blob", LexiconTypeKind.Blob)]
    [InlineData("object", LexiconTypeKind.Object)]
    [InlineData("record", LexiconTypeKind.Record)]
    [InlineData("query", LexiconTypeKind.Query)]
    [InlineData("procedure", LexiconTypeKind.Procedure)]
    [InlineData("subscription", LexiconTypeKind.Subscription)]
    [InlineData("union", LexiconTypeKind.Union)]
    [InlineData("token", LexiconTypeKind.Token)]
    [InlineData("array", LexiconTypeKind.Array)]
    [InlineData("ref", LexiconTypeKind.Ref)]
    [InlineData("unknown", LexiconTypeKind.Any)]
    public void RegisterDocument_SetsCorrectTypeKind(string lexiconType, LexiconTypeKind expectedKind)
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "test.type",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition { Type = lexiconType }
            }
        };

        registry.RegisterDocument(doc);

        var typeInfo = registry.Lookup("test.type");
        Assert.NotNull(typeInfo);
        Assert.Equal(expectedKind, typeInfo.Kind);
    }

    [Theory]
    [InlineData("string", "string")]
    [InlineData("boolean", "bool")]
    [InlineData("integer", "long")]
    [InlineData("bytes", "byte[]")]
    [InlineData("cid-link", "CarpaNet.ATCid")]
    [InlineData("blob", "CarpaNet.ATBlob")]
    [InlineData("unknown", "System.Text.Json.JsonElement")]
    public void ResolveToCSharpType_MapsPrimitiveTypes(string lexiconType, string expectedCSharpType)
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "test.primitive",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition { Type = lexiconType }
            }
        };

        registry.RegisterDocument(doc);

        var result = registry.ResolveToCSharpType("test.primitive", "test.other");
        Assert.Equal(expectedCSharpType, result);
    }

    [Theory]
    [InlineData("at-identifier", "CarpaNet.ATIdentifier")]
    [InlineData("at-uri", "CarpaNet.ATUri")]
    [InlineData("datetime", "System.DateTimeOffset")]
    [InlineData("did", "CarpaNet.ATDid")]
    [InlineData("handle", "CarpaNet.ATHandle")]
    public void ResolveToCSharpType_MapsStringFormats(string format, string expectedCSharpType)
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "test.string",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition { Type = "string", Format = format }
            }
        };

        registry.RegisterDocument(doc);

        var result = registry.ResolveToCSharpType("test.string", "test.other");
        Assert.Equal(expectedCSharpType, result);
    }

    [Fact]
    public void ResolveToCSharpType_ResolvesObjectType()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "app.bsky.feed.defs",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["feedViewPost"] = new LexiconDefinition { Type = "object" }
            }
        };

        registry.RegisterDocument(doc);

        var result = registry.ResolveToCSharpType("app.bsky.feed.defs#feedViewPost", "app.bsky.feed.post");
        Assert.Equal("AppBsky.Feed.DefsFeedViewPost", result);
    }

    [Fact]
    public void ResolveToCSharpType_ResolvesUnionAsInterface()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "app.bsky.embed",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["embedUnion"] = new LexiconDefinition
                {
                    Type = "union",
                    Refs = new List<string> { "app.bsky.embed.images", "app.bsky.embed.video" }
                }
            }
        };

        registry.RegisterDocument(doc);

        var result = registry.ResolveToCSharpType("app.bsky.embed#embedUnion", "app.bsky.feed.post");
        Assert.Equal("IEmbedEmbedUnion", result);
    }

    [Fact]
    public void ResolveToCSharpType_ResolvesLocalRef()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "app.bsky.feed.post",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition { Type = "record" },
                ["entity"] = new LexiconDefinition { Type = "object" }
            }
        };

        registry.RegisterDocument(doc);

        var result = registry.ResolveToCSharpType("#entity", "app.bsky.feed.post");
        Assert.Equal("AppBsky.Feed.PostEntity", result);
    }

    [Fact]
    public void RefGeneratesClass_ReturnsTrueForObjectType()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "test.type",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition { Type = "object" }
            }
        };

        registry.RegisterDocument(doc);

        Assert.True(registry.RefGeneratesClass("test.type", "other.type"));
    }

    [Fact]
    public void RefGeneratesClass_ReturnsFalseForPrimitiveType()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "test.type",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition { Type = "string" }
            }
        };

        registry.RegisterDocument(doc);

        Assert.False(registry.RefGeneratesClass("test.type", "other.type"));
    }

    [Fact]
    public void RefIsUnion_ReturnsTrueForUnionType()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "test.type",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "union",
                    Refs = new List<string> { "test.a", "test.b" }
                }
            }
        };

        registry.RegisterDocument(doc);

        Assert.True(registry.RefIsUnion("test.type", "other.type"));
    }

    [Fact]
    public void TypeRegistry_WithRootNamespace_PrefixesNamespace()
    {
        var registry = new TypeRegistry("MyApp");
        var doc = new LexiconDocument
        {
            Id = "app.bsky.feed.post",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition { Type = "record" }
            }
        };

        registry.RegisterDocument(doc);

        var typeInfo = registry.Lookup("app.bsky.feed.post");
        Assert.NotNull(typeInfo);
        Assert.Equal("MyApp.AppBsky.Feed", typeInfo.CSharpNamespace);
    }
}
