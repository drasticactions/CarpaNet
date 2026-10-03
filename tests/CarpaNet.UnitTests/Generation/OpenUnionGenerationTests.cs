using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CarpaNet.Cbor;
using Xunit;

namespace CarpaNet.UnitTests.Generation;

/// <summary>
/// End-to-end tests for open unions: unknown members must survive JSON and CBOR round trips.
/// </summary>
public class OpenUnionGenerationTests
{
    // Shapes mirror real lexicons: a single open union (post embed), an inline array of open unions
    // (facet features), a named array-of-union def (app.bsky.actor.defs#preferences) and a closed union.
    private const string Lexicon = """
        {
          "lexicon": 1,
          "id": "com.example.unions",
          "defs": {
            "main": {
              "type": "record",
              "key": "tid",
              "record": {
                "type": "object",
                "properties": {
                  "embed": { "type": "union", "refs": ["#a", "#b"] },
                  "items": { "type": "array", "items": { "type": "union", "refs": ["#a", "#b"] } },
                  "prefs": { "type": "ref", "ref": "#preferences" },
                  "closedEmbed": { "type": "union", "refs": ["#a", "#b"], "closed": true }
                }
              }
            },
            "a": {
              "type": "object",
              "required": ["text"],
              "properties": { "text": { "type": "string" } }
            },
            "b": {
              "type": "object",
              "properties": { "count": { "type": "integer" } }
            },
            "preferences": {
              "type": "array",
              "items": { "type": "union", "refs": ["#a", "#b"] }
            }
          }
        }
        """;

    private const string RecordJson = """
        {
          "$type": "com.example.unions",
          "embed": { "$type": "com.example.future#thing", "x": 1, "nested": { "y": [1, 2, "three"], "flag": true } },
          "items": [
            { "$type": "com.example.unions#a", "text": "hi" },
            { "$type": "com.example.future#other", "z": "q" },
            { "$type": "com.example.unions#b", "count": 3 },
            { "noType": true }
          ],
          "prefs": [
            { "$type": "com.example.future#newPref", "enabled": false, "list": ["a", "b"] },
            { "$type": "com.example.unions#a", "text": "known" }
          ]
        }
        """;

    private static readonly Lazy<Assembly> Generated = new(() =>
        GeneratorTestHarness.CompileAndLoad(GeneratorTestHarness.Run(new[] { Lexicon })));

    [Fact]
    public void OpenUnion_GeneratesUnknownClassImplementingInterface()
    {
        var assembly = Generated.Value;

        foreach (var (iface, unknown) in new[]
        {
            ("ComExample.IUnionsEmbed", "ComExample.Unknown_UnionsEmbed"),
            ("ComExample.IUnionsItems", "ComExample.Unknown_UnionsItems"),
            ("ComExample.IUnionsPreferences", "ComExample.Unknown_UnionsPreferences"),
        })
        {
            var ifaceType = assembly.GetType(iface, throwOnError: true)!;
            var unknownType = assembly.GetType(unknown, throwOnError: true)!;
            Assert.True(unknownType.IsSealed);
            Assert.True(ifaceType.IsAssignableFrom(unknownType));
            Assert.NotNull(unknownType.GetConstructor(new[] { typeof(string), typeof(JsonElement), typeof(byte[]) }));
        }

        // Closed unions keep throwing on unknown members and get no Unknown_ class
        Assert.Null(assembly.GetType("ComExample.Unknown_UnionsClosedEmbed"));
    }

    [Fact]
    public void Json_UnknownMembersArePreservedInOrder()
    {
        var record = DeserializeRecord(RecordJson);
        var recordType = record.GetType();

        var embed = recordType.GetProperty("Embed")!.GetValue(record)!;
        Assert.Equal("Unknown_UnionsEmbed", embed.GetType().Name);
        Assert.Equal("com.example.future#thing", GetUnknownType(embed));
        AssertJsonEquivalent(
            """{ "$type": "com.example.future#thing", "x": 1, "nested": { "y": [1, 2, "three"], "flag": true } }""",
            GetUnknownRaw(embed).GetRawText());

        var items = ((IEnumerable)recordType.GetProperty("Items")!.GetValue(record)!).Cast<object>().ToList();
        Assert.Equal(4, items.Count);
        Assert.Equal("UnionsA", items[0].GetType().Name);
        Assert.Equal("Unknown_UnionsItems", items[1].GetType().Name);
        Assert.Equal("com.example.future#other", GetUnknownType(items[1]));
        Assert.Equal("UnionsB", items[2].GetType().Name);
        Assert.Equal("Unknown_UnionsItems", items[3].GetType().Name);
        Assert.Equal(string.Empty, GetUnknownType(items[3]));

        var prefs = ((IEnumerable)recordType.GetProperty("Prefs")!.GetValue(record)!).Cast<object>().ToList();
        Assert.Equal(2, prefs.Count);
        Assert.Equal("Unknown_UnionsPreferences", prefs[0].GetType().Name);
        Assert.Equal("com.example.future#newPref", GetUnknownType(prefs[0]));
        Assert.Equal("UnionsA", prefs[1].GetType().Name);
    }

    [Fact]
    public void Json_RoundTripIsLossless()
    {
        var record = DeserializeRecord(RecordJson);

        var json = JsonSerializer.Serialize(record, record.GetType(), GetJsonOptions());

        AssertJsonEquivalent(RecordJson, json);
    }

    [Fact]
    public void Json_ClosedUnion_UnknownMemberStillThrows()
    {
        const string json = """
            { "$type": "com.example.unions", "closedEmbed": { "$type": "com.example.future#thing", "x": 1 } }
            """;

        Assert.ThrowsAny<Exception>(() => DeserializeRecord(json));
    }

    [Fact]
    public void Json_CallerConstructedUnknownMember_IsWrittenVerbatim()
    {
        var assembly = Generated.Value;
        var recordType = assembly.GetType("ComExample.Unions", throwOnError: true)!;
        var unknownType = assembly.GetType("ComExample.Unknown_UnionsEmbed", throwOnError: true)!;

        using var doc = JsonDocument.Parse("""{ "$type": "com.example.custom#x", "value": 42 }""");
        var unknown = Activator.CreateInstance(unknownType, "com.example.custom#x", doc.RootElement, null)!;

        // The constructor clones, so the instance outlives the source document
        doc.Dispose();

        var record = DeserializeRecord("""{ "$type": "com.example.unions" }""");
        recordType.GetProperty("Embed")!.SetValue(record, unknown);

        var json = JsonSerializer.Serialize(record, recordType, GetJsonOptions());
        AssertJsonEquivalent(
            """{ "$type": "com.example.unions", "embed": { "$type": "com.example.custom#x", "value": 42 } }""",
            json);

        // ToJson/FromJson helpers on the unknown class
        var toJson = (JsonElement)unknownType.GetMethod("ToJson")!.Invoke(unknown, null)!;
        Assert.Equal(42, toJson.GetProperty("value").GetInt32());
        var fromJson = unknownType.GetMethod("FromJson")!.Invoke(null, new object[] { toJson })!;
        Assert.Equal("com.example.custom#x", GetUnknownType(fromJson));
    }

    [Fact]
    public void Cbor_UnknownMembersRoundTripWithRawBytes()
    {
        var record = DeserializeRecord(RecordJson);
        var recordType = record.GetType();
        var cborContext = GetCborContext();

        // JSON-sourced unknowns are converted to CBOR from their JSON value
        var firstBytes = CborSerialize(cborContext, recordType, record);

        var decoded = CborDeserialize(cborContext, recordType, firstBytes);
        var embed = recordType.GetProperty("Embed")!.GetValue(decoded)!;
        Assert.Equal("Unknown_UnionsEmbed", embed.GetType().Name);
        Assert.Equal("com.example.future#thing", GetUnknownType(embed));
        Assert.NotNull(embed.GetType().GetProperty("RawCbor")!.GetValue(embed));
        AssertJsonEquivalent(
            """{ "$type": "com.example.future#thing", "x": 1, "nested": { "y": [1, 2, "three"], "flag": true } }""",
            GetUnknownRaw(embed).GetRawText());

        var items = ((IEnumerable)recordType.GetProperty("Items")!.GetValue(decoded)!).Cast<object>().ToList();
        Assert.Equal(
            new[] { "UnionsA", "Unknown_UnionsItems", "UnionsB", "Unknown_UnionsItems" },
            items.Select(i => i.GetType().Name));
        Assert.Equal("com.example.future#other", GetUnknownType(items[1]));
        Assert.Equal(string.Empty, GetUnknownType(items[3]));

        var prefs = ((IEnumerable)recordType.GetProperty("Prefs")!.GetValue(decoded)!).Cast<object>().ToList();
        Assert.Equal(new[] { "Unknown_UnionsPreferences", "UnionsA" }, prefs.Select(p => p.GetType().Name));

        // Converting back to JSON gives the original document
        AssertJsonEquivalent(RecordJson, JsonSerializer.Serialize(decoded, recordType, GetJsonOptions()));

        // CBOR-sourced unknowns are written back from their original bytes
        var secondBytes = CborSerialize(cborContext, recordType, decoded);
        Assert.Equal(firstBytes, secondBytes);
    }

    [Fact]
    public void Cbor_ClosedUnion_KnownMemberRoundTrips_UnknownMemberThrows()
    {
        var recordType = Generated.Value.GetType("ComExample.Unions", throwOnError: true)!;
        var cborContext = GetCborContext();

        var record = DeserializeRecord("""
            { "$type": "com.example.unions", "closedEmbed": { "$type": "com.example.unions#b", "count": 7 }, "items": [] }
            """);
        var decoded = CborDeserialize(cborContext, recordType, CborSerialize(cborContext, recordType, record));
        Assert.Equal("UnionsB", recordType.GetProperty("ClosedEmbed")!.GetValue(decoded)!.GetType().Name);

        var writer = new DagCborWriter();
        writer.WriteStartMap(2);
        writer.WriteTextString("$type");
        writer.WriteTextString("com.example.unions");
        writer.WriteTextString("closedEmbed");
        writer.WriteStartMap(1);
        writer.WriteTextString("$type");
        writer.WriteTextString("com.example.future#thing");
        writer.WriteEndMap();
        writer.WriteEndMap();
        var bytes = writer.Encode();

        var ex = Assert.ThrowsAny<Exception>(() => CborDeserialize(cborContext, recordType, bytes));
        Assert.IsType<InvalidOperationException>(ex is TargetInvocationException tie ? tie.InnerException : ex);
    }

    private static object DeserializeRecord(string json)
    {
        var recordType = Generated.Value.GetType("ComExample.Unions", throwOnError: true)!;
        return JsonSerializer.Deserialize(json, recordType, GetJsonOptions())!;
    }

    private static JsonSerializerOptions GetJsonOptions()
    {
        var contextType = Generated.Value.GetType("CarpaNet.Json.ATProtoJsonContext", throwOnError: true)!;
        return (JsonSerializerOptions)contextType.GetProperty("DefaultOptions")!.GetValue(null)!;
    }

    private static CborSerializerContext GetCborContext()
    {
        var contextType = Generated.Value.GetType("CarpaNet.Cbor.ATProtoCborContext", throwOnError: true)!;
        return (CborSerializerContext)contextType.GetProperty("Default")!.GetValue(null)!;
    }

    private static byte[] CborSerialize(CborSerializerContext context, Type type, object value)
    {
        var method = typeof(CborSerializerContext).GetMethod(nameof(CborSerializerContext.Serialize))!.MakeGenericMethod(type);
        return (byte[])method.Invoke(context, new[] { value })!;
    }

    private static object CborDeserialize(CborSerializerContext context, Type type, byte[] data)
    {
        var method = typeof(CborSerializerContext).GetMethod(nameof(CborSerializerContext.Deserialize))!.MakeGenericMethod(type);
        return method.Invoke(context, new object[] { new ReadOnlyMemory<byte>(data) })!;
    }

    private static string GetUnknownType(object unknown)
        => (string)unknown.GetType().GetProperty("Type")!.GetValue(unknown)!;

    private static JsonElement GetUnknownRaw(object unknown)
        => (JsonElement)unknown.GetType().GetProperty("Raw")!.GetValue(unknown)!;

    private static void AssertJsonEquivalent(string expected, string actual)
    {
        var expectedNode = JsonNode.Parse(expected);
        var actualNode = JsonNode.Parse(actual);
        Assert.True(JsonNode.DeepEquals(expectedNode, actualNode), $"JSON differs.\nExpected: {expected}\nActual:   {actual}");
    }
}
