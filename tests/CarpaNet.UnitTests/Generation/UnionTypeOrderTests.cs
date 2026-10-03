using System.Collections;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace CarpaNet.UnitTests.Generation;

/// <summary>
/// A server may write a union member's <c>$type</c> after its other properties (the reference PDS
/// does so in <c>com.atproto.repo.applyWrites</c> results). The generated default options must read
/// such closed (polymorphic) unions.
/// </summary>
public class UnionTypeOrderTests
{
    private const string Lexicon = """
        {
          "lexicon": 1,
          "id": "com.example.writes",
          "defs": {
            "main": {
              "type": "query",
              "output": {
                "encoding": "application/json",
                "schema": {
                  "type": "object",
                  "required": ["results"],
                  "properties": {
                    "results": { "type": "array", "items": { "type": "union", "refs": ["#createResult", "#deleteResult"], "closed": true } }
                  }
                }
              }
            },
            "createResult": {
              "type": "object",
              "required": ["uri"],
              "properties": { "uri": { "type": "string" } }
            },
            "deleteResult": { "type": "object", "properties": {} }
          }
        }
        """;

    private static readonly Lazy<Assembly> Generated = new(() =>
        GeneratorTestHarness.CompileAndLoad(GeneratorTestHarness.Run(new[] { Lexicon })));

    [Fact]
    public void DefaultOptions_ReadClosedUnionMembersWhoseTypeComesLast()
    {
        const string json = """
            {"results":[{"uri":"at://did:plc:x/app.bsky.feed.post/1","$type":"com.example.writes#createResult"},{"$type":"com.example.writes#deleteResult"}]}
            """;

        var outputType = Generated.Value.GetTypes().Single(t => t.Name.EndsWith("Output", StringComparison.Ordinal));
        var output = JsonSerializer.Deserialize(json, outputType, GetJsonOptions())!;

        var results = ((IEnumerable)outputType.GetProperty("Results")!.GetValue(output)!).Cast<object>().ToList();
        Assert.Equal(2, results.Count);
        Assert.EndsWith("CreateResult", results[0].GetType().Name);
        Assert.Equal("at://did:plc:x/app.bsky.feed.post/1", results[0].GetType().GetProperty("Uri")!.GetValue(results[0])!.ToString());
        Assert.EndsWith("DeleteResult", results[1].GetType().Name);
    }

    [Fact]
    public void DefaultOptions_AllowOutOfOrderMetadata()
    {
        Assert.True(GetJsonOptions().AllowOutOfOrderMetadataProperties);
    }

    private static JsonSerializerOptions GetJsonOptions()
    {
        var contextType = Generated.Value.GetType("CarpaNet.Json.ATProtoJsonContext", throwOnError: true)!;
        return (JsonSerializerOptions)contextType.GetProperty("DefaultOptions")!.GetValue(null)!;
    }
}
