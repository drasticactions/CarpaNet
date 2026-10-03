using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CarpaNet.Cbor;
using Xunit;

namespace CarpaNet.UnitTests.Generation;

/// <summary>
/// End-to-end tests for the generated CBOR and JSON contexts: records must encode as canonical DAG-CBOR
/// (so locally computed CIDs match atproto's) and datetimes must use the atproto form.
/// </summary>
public class CanonicalRecordEncodingTests
{
    // Trimmed copies of the real lexicons, keeping their property declaration order
    // (app.bsky.feed.post declares text ... createdAt, which is not canonical DAG-CBOR order).
    private const string PostLexicon = """
        {
          "lexicon": 1,
          "id": "app.bsky.feed.post",
          "defs": {
            "main": {
              "type": "record",
              "key": "tid",
              "record": {
                "type": "object",
                "required": ["text", "createdAt"],
                "properties": {
                  "text": { "type": "string", "maxLength": 3000, "maxGraphemes": 300 },
                  "reply": { "type": "ref", "ref": "#replyRef" },
                  "embed": { "type": "union", "refs": ["app.bsky.embed.images"] },
                  "langs": { "type": "array", "maxLength": 3, "items": { "type": "string", "format": "language" } },
                  "tags": { "type": "array", "maxLength": 8, "items": { "type": "string" } },
                  "createdAt": { "type": "string", "format": "datetime" }
                }
              }
            },
            "replyRef": {
              "type": "object",
              "required": ["root", "parent"],
              "properties": {
                "root": { "type": "ref", "ref": "com.atproto.repo.strongRef" },
                "parent": { "type": "ref", "ref": "com.atproto.repo.strongRef" }
              }
            }
          }
        }
        """;

    private const string ImagesLexicon = """
        {
          "lexicon": 1,
          "id": "app.bsky.embed.images",
          "defs": {
            "main": {
              "type": "object",
              "required": ["images"],
              "properties": {
                "images": { "type": "array", "items": { "type": "ref", "ref": "#image" }, "maxLength": 4 }
              }
            },
            "image": {
              "type": "object",
              "required": ["image", "alt"],
              "properties": {
                "image": { "type": "blob", "accept": ["image/*"], "maxSize": 1000000 },
                "alt": { "type": "string" },
                "aspectRatio": { "type": "ref", "ref": "app.bsky.embed.defs#aspectRatio" }
              }
            }
          }
        }
        """;

    private const string EmbedDefsLexicon = """
        {
          "lexicon": 1,
          "id": "app.bsky.embed.defs",
          "defs": {
            "aspectRatio": {
              "type": "object",
              "required": ["width", "height"],
              "properties": {
                "width": { "type": "integer", "minimum": 1 },
                "height": { "type": "integer", "minimum": 1 }
              }
            }
          }
        }
        """;

    private const string StrongRefLexicon = """
        {
          "lexicon": 1,
          "id": "com.atproto.repo.strongRef",
          "defs": {
            "main": {
              "type": "object",
              "required": ["uri", "cid"],
              "properties": {
                "uri": { "type": "string", "format": "at-uri" },
                "cid": { "type": "string", "format": "cid" }
              }
            }
          }
        }
        """;

    private const string QueryLexicon = """
        {
          "lexicon": 1,
          "id": "com.example.getSince",
          "defs": {
            "main": {
              "type": "query",
              "parameters": {
                "type": "params",
                "required": ["since"],
                "properties": {
                  "since": { "type": "string", "format": "datetime" },
                  "until": { "type": "string", "format": "datetime" }
                }
              },
              "output": { "encoding": "application/json", "schema": { "type": "object", "properties": {} } }
            }
          }
        }
        """;

    // A fixed CIDv1/raw/sha256 used as a stable blob fixture (from social-app's computeCid.test.ts)
    private const string BlobCid = "bafkreieq5jui4j25lacwomsqgjeswwl3y5zcdrresptwgmfylxo2depppq";

    private static readonly Lazy<GeneratorTestHarness.GeneratorRun> Run = new(() =>
        GeneratorTestHarness.Run(new[] { PostLexicon, ImagesLexicon, EmbedDefsLexicon, StrongRefLexicon, QueryLexicon }));

    private static readonly Lazy<Assembly> Generated = new(() => GeneratorTestHarness.CompileAndLoad(Run.Value));

    // Golden CIDs from social-app src/lib/api/__tests__/computeCid.test.ts (computed with @ipld/dag-cbor)
    [Fact]
    public void GoldenCid_PlainPostRecord()
    {
        var record = Post("""{ "$type": "app.bsky.feed.post", "createdAt": "2024-01-01T00:00:00.000Z", "text": "hello world" }""");

        Assert.Equal("bafyreieawtmh7hwfrqpamqkodza5r62bbfhsepe2iyustgxhgbhi6b2lfi", ComputeCid(record));
    }

    [Fact]
    public void GoldenCid_PostWithImageEmbedAndBlob()
    {
        var record = Post($$"""
            {
              "$type": "app.bsky.feed.post",
              "createdAt": "2024-01-01T00:00:00.001Z",
              "text": "post with image",
              "embed": {
                "$type": "app.bsky.embed.images",
                "images": [
                  {
                    "image": { "$type": "blob", "ref": { "$link": "{{BlobCid}}" }, "mimeType": "image/jpeg", "size": 12345 },
                    "alt": "alt text",
                    "aspectRatio": { "width": 100, "height": 200 }
                  }
                ]
              }
            }
            """);

        Assert.Equal("bafyreiem7g6vja66nebr7he4fshfnlyndyldbvle2n265oixscmepjcbii", ComputeCid(record));
    }

    [Fact]
    public void GoldenCid_ThreePostThreadChainsReplyStrongRefs()
    {
        const string did = "did:plc:abc123";
        var golden = new[]
        {
            "bafyreig62rxs34h5rvznfrracwkjlfgad5b25qxglp2hcziqdfas2nw2ee",
            "bafyreicxcj2tq5jrh5jcaczg3eli5cvxitgzu7kpu3fm5v3njq2byjxirq",
            "bafyreigvaswuhlpd7dllja2xrqswhqbruyv2kar7mvbn7gdm5ldzu6vkti",
        };

        string? root = null;
        string? parent = null;
        for (var i = 0; i < 3; i++)
        {
            var reply = parent == null ? string.Empty : $", \"reply\": {{ \"root\": {root}, \"parent\": {parent} }}";
            var record = Post($"{{ \"$type\": \"app.bsky.feed.post\", \"createdAt\": \"2024-01-01T00:00:00.00{i}Z\", \"text\": \"post {i}\"{reply} }}");

            var cid = ComputeCid(record);
            Assert.Equal(golden[i], cid);

            var strongRef = $"{{ \"cid\": \"{cid}\", \"uri\": \"at://{did}/app.bsky.feed.post/rkey{i}\" }}";
            root ??= strongRef;
            parent = strongRef;
        }
    }

    [Fact]
    public void Cbor_RecordKeysAreCanonical_ForAnySubsetOfOptionalFields()
    {
        var full = Post($$"""
            {
              "$type": "app.bsky.feed.post",
              "createdAt": "2024-01-01T00:00:00.000Z",
              "text": "t",
              "langs": ["en"],
              "tags": ["x"],
              "reply": {
                "root": { "cid": "{{BlobCid}}", "uri": "at://did:plc:abc/app.bsky.feed.post/1" },
                "parent": { "cid": "{{BlobCid}}", "uri": "at://did:plc:abc/app.bsky.feed.post/1" }
              },
              "embed": { "$type": "app.bsky.embed.images", "images": [] }
            }
            """);
        Assert.Equal(new[] { "tags", "text", "$type", "embed", "langs", "reply", "createdAt" }, TopLevelKeys(Serialize(full)));

        var partial = Post("""{ "$type": "app.bsky.feed.post", "createdAt": "2024-01-01T00:00:00.000Z", "text": "t", "langs": ["en"] }""");
        Assert.Equal(new[] { "text", "$type", "langs", "createdAt" }, TopLevelKeys(Serialize(partial)));
    }

    [Fact]
    public void Cbor_UnionMemberWithoutOwnTypeGetsTypeAtCanonicalPosition()
    {
        var record = Post($$"""
            {
              "$type": "app.bsky.feed.post",
              "createdAt": "2024-01-01T00:00:00.000Z",
              "text": "t",
              "embed": {
                "$type": "app.bsky.embed.images",
                "images": [ { "image": { "$type": "blob", "ref": { "$link": "{{BlobCid}}" }, "mimeType": "image/png", "size": 1 }, "alt": "a" } ]
              }
            }
            """);

        var reader = new DagCborReader(Serialize(record));
        var count = reader.ReadStartMap();
        for (var i = 0; i < count; i++)
        {
            if (reader.ReadTextString() != "embed")
            {
                reader.SkipValue();
                continue;
            }

            // "$type" (5 bytes) sorts before "images" (6 bytes)
            Assert.Equal(2, reader.ReadStartMap());
            Assert.Equal("$type", reader.ReadTextString());
            Assert.Equal("app.bsky.embed.images", reader.ReadTextString());
            Assert.Equal("images", reader.ReadTextString());
            Assert.Equal(1, reader.ReadStartArray());

            // image object: alt, image; blob: ref, size, $type, mimeType
            Assert.Equal(2, reader.ReadStartMap());
            Assert.Equal("alt", reader.ReadTextString());
            reader.SkipValue();
            Assert.Equal("image", reader.ReadTextString());
            Assert.Equal(4, reader.ReadStartMap());
            Assert.Equal("ref", reader.ReadTextString());
            reader.SkipValue();
            Assert.Equal("size", reader.ReadTextString());
            reader.SkipValue();
            Assert.Equal("$type", reader.ReadTextString());
            reader.SkipValue();
            Assert.Equal("mimeType", reader.ReadTextString());
            return;
        }

        Assert.Fail("embed not found");
    }

    [Fact]
    public void Cbor_RecordRoundTrips()
    {
        var record = Post("""{ "$type": "app.bsky.feed.post", "createdAt": "2024-05-06T07:08:09.123Z", "text": "hi", "langs": ["en", "ja"] }""");
        var bytes = Serialize(record);

        var decoded = Deserialize(record.GetType(), bytes);
        Assert.Equal(bytes, Serialize(decoded));
        Assert.Equal(
            new DateTimeOffset(2024, 5, 6, 7, 8, 9, 123, TimeSpan.Zero),
            (DateTimeOffset)record.GetType().GetProperty("CreatedAt")!.GetValue(decoded)!);
    }

    [Theory]
    [InlineData("2024-01-01T00:00:00.000Z", "2024-01-01T00:00:00.000Z")]
    [InlineData("2024-01-01T00:00:00Z", "2024-01-01T00:00:00.000Z")]
    [InlineData("2024-01-01T00:00:00.1234567Z", "2024-01-01T00:00:00.123Z")]
    [InlineData("2024-01-01T00:00:00.123456789Z", "2024-01-01T00:00:00.123Z")]
    [InlineData("2024-01-01T02:00:00.999+02:00", "2024-01-01T00:00:00.999Z")]
    [InlineData("2023-12-31T19:30:00-04:30", "2024-01-01T00:00:00.000Z")]
    [InlineData("2024-01-01T00:00:00.000-00:00", "2024-01-01T00:00:00.000Z")]
    public void DateTime_JsonAndCborWriteAtprotoForm(string input, string expected)
    {
        var record = Post($"{{ \"$type\": \"app.bsky.feed.post\", \"createdAt\": \"{input}\", \"text\": \"t\" }}");

        var json = JsonSerializer.Serialize(record, record.GetType(), GetJsonOptions());
        using (var doc = JsonDocument.Parse(json))
        {
            Assert.Equal(expected, doc.RootElement.GetProperty("createdAt").GetString());
        }

        var reader = new DagCborReader(Serialize(record));
        var count = reader.ReadStartMap();
        for (var i = 0; i < count; i++)
        {
            if (reader.ReadTextString() == "createdAt")
            {
                Assert.Equal(expected, reader.ReadTextString());
                return;
            }

            reader.SkipValue();
        }

        Assert.Fail("createdAt not found");
    }

    [Fact]
    public void DateTime_InvalidJsonValueThrows()
    {
        Assert.ThrowsAny<JsonException>(() => Post("""{ "$type": "app.bsky.feed.post", "createdAt": "not a date", "text": "t" }"""));
    }

    [Fact]
    public void DateTime_QueryParametersUseAtprotoForm()
    {
        var source = Run.Value.AllSource;
        Assert.Contains("global::CarpaNet.ATDateTimeJsonConverter.Format(Since)", source);
        Assert.Contains("global::CarpaNet.ATDateTimeJsonConverter.Format(Until.Value)", source);
        Assert.DoesNotContain("ToString(\"o\")", source);
    }

    private static object Post(string json)
    {
        var type = Generated.Value.GetType("AppBsky.Feed.Post", throwOnError: true)!;
        return JsonSerializer.Deserialize(json, type, GetJsonOptions())!;
    }

    private static string ComputeCid(object record) => ATCid.FromSha256Hash(SHA256.HashData(Serialize(record))).Value;

    private static List<string> TopLevelKeys(byte[] cbor)
    {
        var reader = new DagCborReader(cbor);
        var count = reader.ReadStartMap() ?? 0;
        var keys = new List<string>();
        for (var i = 0; i < count; i++)
        {
            keys.Add(reader.ReadTextString());
            reader.SkipValue();
        }

        return keys;
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

    private static byte[] Serialize(object value)
    {
        var method = typeof(CborSerializerContext).GetMethod(nameof(CborSerializerContext.Serialize))!.MakeGenericMethod(value.GetType());
        return (byte[])method.Invoke(GetCborContext(), new[] { value })!;
    }

    private static object Deserialize(Type type, byte[] data)
    {
        var method = typeof(CborSerializerContext).GetMethod(nameof(CborSerializerContext.Deserialize))!.MakeGenericMethod(type);
        return method.Invoke(GetCborContext(), new object[] { new ReadOnlyMemory<byte>(data) })!;
    }
}
