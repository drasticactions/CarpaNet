using System.Collections;
using System.Reflection;
using System.Text.Json;
using CarpaNet.Cbor;
using Xunit;

namespace CarpaNet.UnitTests.Generation;

/// <summary>
/// A lexicon may reference a definition that is not loaded (a published schema that names a
/// definition its authority never published). The generated code must still compile: the value is
/// kept as raw JSON, and union members that name it become unknown members.
/// </summary>
public class DanglingRefTests
{
    private const string Lexicon = """
        {
          "lexicon": 1,
          "id": "com.example.dangling",
          "defs": {
            "main": {
              "type": "query",
              "output": {
                "encoding": "application/json",
                "schema": {
                  "type": "object",
                  "required": ["group", "items"],
                  "properties": {
                    "group": { "type": "ref", "ref": "com.example.missing#groupView" },
                    "items": { "type": "array", "items": { "type": "ref", "ref": "com.example.missing#itemView" } },
                    "open": { "type": "union", "refs": ["#known", "com.example.missing#other"] },
                    "closed": { "type": "union", "refs": ["#known", "com.example.missing#other"], "closed": true },
                    "local": { "type": "ref", "ref": "#notDefinedHere" }
                  }
                }
              }
            },
            "known": {
              "type": "object",
              "required": ["name"],
              "properties": { "name": { "type": "string" } }
            }
          }
        }
        """;

    private static readonly Lazy<GeneratorTestHarness.GeneratorRun> Run = new(() =>
        GeneratorTestHarness.Run(new[] { Lexicon }));

    private static readonly Lazy<Assembly> Generated = new(() => GeneratorTestHarness.CompileAndLoad(Run.Value));

    [Fact]
    public void Compiles_AndReportsTheUnresolvedReferences()
    {
        Assert.NotNull(Generated.Value);
        var warning = Assert.Single(Run.Value.GeneratorDiagnostics, d => d.Id == "ATPG002");
        Assert.Contains("com.example.missing", warning.GetMessage());
    }

    [Fact]
    public void DanglingRefs_AreRawJson()
    {
        var output = OutputType();
        Assert.Equal(typeof(JsonElement), output.GetProperty("Group")!.PropertyType);
        Assert.Equal(typeof(List<JsonElement>), output.GetProperty("Items")!.PropertyType);
        var local = output.GetProperty("Local")!.PropertyType;
        Assert.True(local == typeof(JsonElement) || local == typeof(JsonElement?));
    }

    [Fact]
    public void Json_RoundTripsTheRawValues_AndUnknownUnionMembers()
    {
        const string json = """
            {"group":{"name":"g","extra":[1,2]},"items":[{"a":1},{"b":"x"}],
             "open":{"$type":"com.example.missing#other","v":1},
             "closed":{"$type":"com.example.dangling#known","name":"k"},
             "local":{"z":true}}
            """;
        var output = JsonSerializer.Deserialize(json, OutputType(), JsonOptions())!;

        var group = (JsonElement)OutputType().GetProperty("Group")!.GetValue(output)!;
        Assert.Equal("g", group.GetProperty("name").GetString());
        var items = ((IEnumerable)OutputType().GetProperty("Items")!.GetValue(output)!).Cast<JsonElement>().ToList();
        Assert.Equal(2, items.Count);
        var open = OutputType().GetProperty("Open")!.GetValue(output)!;
        Assert.StartsWith("Unknown_", open.GetType().Name);
        var closed = OutputType().GetProperty("Closed")!.GetValue(output)!;
        Assert.EndsWith("Known", closed.GetType().Name);

        var written = JsonSerializer.Serialize(output, OutputType(), JsonOptions());
        using var doc = JsonDocument.Parse(written);
        Assert.Equal("g", doc.RootElement.GetProperty("group").GetProperty("name").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("items")[0].GetProperty("a").GetInt32());
        Assert.Equal("com.example.missing#other", doc.RootElement.GetProperty("open").GetProperty("$type").GetString());
    }

    [Fact]
    public void Cbor_RoundTripsTheRawValues()
    {
        const string json = """{"group":{"name":"g"},"items":[{"a":1}]}""";
        var output = JsonSerializer.Deserialize(json, OutputType(), JsonOptions())!;

        var context = (CborSerializerContext)Generated.Value.GetType("CarpaNet.Cbor.ATProtoCborContext", throwOnError: true)!
            .GetProperty("Default")!.GetValue(null)!;
        var serialize = typeof(CborSerializerContext).GetMethod(nameof(CborSerializerContext.Serialize))!.MakeGenericMethod(OutputType());
        var bytes = (byte[])serialize.Invoke(context, new[] { output })!;
        var deserialize = typeof(CborSerializerContext).GetMethods().First(m => m.Name == nameof(CborSerializerContext.Deserialize) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(ReadOnlyMemory<byte>)).MakeGenericMethod(OutputType());
        var back = deserialize.Invoke(context, new object[] { new ReadOnlyMemory<byte>(bytes) })!;

        var group = (JsonElement)OutputType().GetProperty("Group")!.GetValue(back)!;
        Assert.Equal("g", group.GetProperty("name").GetString());
    }

    private static Type OutputType() =>
        Generated.Value.GetTypes().Single(t => t.Name == "DanglingOutput");

    private static JsonSerializerOptions JsonOptions()
    {
        var contextType = Generated.Value.GetType("CarpaNet.Json.ATProtoJsonContext", throwOnError: true)!;
        return (JsonSerializerOptions)contextType.GetProperty("DefaultOptions")!.GetValue(null)!;
    }
}
