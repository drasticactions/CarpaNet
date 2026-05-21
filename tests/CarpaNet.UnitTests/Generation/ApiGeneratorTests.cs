using CarpaNet.Generation;
using CarpaNet.Models;
using Xunit;

namespace CarpaNet.UnitTests.Generation;

public class ApiGeneratorTests
{

    [Fact]
    public void GenerateParametersClass_ArrayProperty_EmitsKeyValuePairForEachItem()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "app.bsky.feed.getPosts",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "query",
                    Parameters = new LexiconDefinition
                    {
                        Type = "params",
                        Properties = new Dictionary<string, LexiconDefinition>
                        {
                            ["uris"] = new LexiconDefinition
                            {
                                Type = "array",
                                Items = new LexiconDefinition { Type = "string", Format = "at-uri" },
                            },
                        },
                        RequiredRaw = CreateJsonArray("uris"),
                    },
                    Output = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition { Type = "object", Properties = new() },
                    },
                },
            },
        };
        registry.RegisterDocument(doc);

        var sb = new SourceBuilder();
        ApiGenerator.GenerateParametersClass(sb, "GetPosts", doc.Defs["main"], doc.Id, registry);
        var generated = sb.ToString();

        // The generated emitter should add a KeyValuePair entry per array item.
        Assert.Contains("new System.Collections.Generic.KeyValuePair<string, string>(\"uris\"", generated);
    }

    private static System.Text.Json.JsonElement CreateJsonArray(params string[] values)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(values);
        return System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
    }
}
