using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CarpaNet.UnitTests.Generation;

/// <summary>
/// Tests the client extension methods generated for procedures with non-JSON bodies or query parameters,
/// and for queries with non-JSON output. Lexicons are copies of the real ones (no network access).
/// </summary>
public class BinaryXrpcGenerationTests
{
    private const string UploadBlob = """
        {
          "lexicon": 1,
          "id": "com.atproto.repo.uploadBlob",
          "defs": {
            "main": {
              "type": "procedure",
              "description": "Upload a new blob, to be referenced from a repository record.",
              "input": { "encoding": "*/*" },
              "output": {
                "encoding": "application/json",
                "schema": {
                  "type": "object",
                  "required": ["blob"],
                  "properties": { "blob": { "type": "blob" } }
                }
              }
            }
          }
        }
        """;

    private const string UploadPart = """
        {
          "lexicon": 1,
          "id": "app.bsky.video.uploadPart",
          "defs": {
            "main": {
              "type": "procedure",
              "description": "Upload one part.",
              "parameters": {
                "type": "params",
                "required": ["jobId", "partNumber"],
                "properties": {
                  "jobId": { "type": "string", "minLength": 1, "maxLength": 256 },
                  "partNumber": { "type": "integer", "minimum": 1 }
                }
              },
              "input": { "encoding": "application/octet-stream" },
              "output": {
                "encoding": "application/json",
                "schema": {
                  "type": "object",
                  "required": ["partNumber", "sizeBytes"],
                  "properties": {
                    "partNumber": { "type": "integer", "minimum": 1 },
                    "sizeBytes": { "type": "integer" }
                  }
                }
              },
              "errors": [ { "name": "UploadNotFound" }, { "name": "PartSizeMismatch" } ]
            }
          }
        }
        """;

    private const string UploadVideo = """
        {
          "lexicon": 1,
          "id": "app.bsky.video.uploadVideo",
          "defs": {
            "main": {
              "type": "procedure",
              "description": "Upload a video to be processed then stored on the PDS.",
              "input": { "encoding": "video/mp4" },
              "output": {
                "encoding": "application/json",
                "schema": {
                  "type": "object",
                  "required": ["jobStatus"],
                  "properties": {
                    "jobStatus": { "type": "ref", "ref": "app.bsky.video.defs#jobStatus" }
                  }
                }
              }
            }
          }
        }
        """;

    private const string VideoDefs = """
        {
          "lexicon": 1,
          "id": "app.bsky.video.defs",
          "defs": {
            "jobStatus": {
              "type": "object",
              "required": ["jobId", "did", "state"],
              "properties": {
                "jobId": { "type": "string" },
                "did": { "type": "string", "format": "did" },
                "state": { "type": "string" }
              }
            }
          }
        }
        """;

    private const string GetBlob = """
        {
          "lexicon": 1,
          "id": "com.atproto.sync.getBlob",
          "defs": {
            "main": {
              "type": "query",
              "description": "Get a blob associated with a given account.",
              "parameters": {
                "type": "params",
                "required": ["did", "cid"],
                "properties": {
                  "did": { "type": "string", "format": "did" },
                  "cid": { "type": "string", "format": "cid" }
                }
              },
              "output": { "encoding": "*/*" },
              "errors": [ { "name": "BlobNotFound" } ]
            }
          }
        }
        """;

    private const string GetRepo = """
        {
          "lexicon": 1,
          "id": "com.atproto.sync.getRepo",
          "defs": {
            "main": {
              "type": "query",
              "description": "Download a repository export as CAR file.",
              "parameters": {
                "type": "params",
                "required": ["did"],
                "properties": {
                  "did": { "type": "string", "format": "did" },
                  "since": { "type": "string", "format": "tid" }
                }
              },
              "output": { "encoding": "application/vnd.ipld.car" }
            }
          }
        }
        """;

    // JSON-bodied procedure that also takes query parameters, plus the unchanged plain cases
    private const string JsonProcedures = """
        {
          "lexicon": 1,
          "id": "com.example.doThing",
          "defs": {
            "main": {
              "type": "procedure",
              "parameters": {
                "type": "params",
                "required": ["mode"],
                "properties": { "mode": { "type": "string" }, "dryRun": { "type": "boolean" } }
              },
              "input": {
                "encoding": "application/json",
                "schema": { "type": "object", "required": ["name"], "properties": { "name": { "type": "string" } } }
              },
              "output": {
                "encoding": "application/json",
                "schema": { "type": "object", "properties": { "ok": { "type": "boolean" } } }
              }
            }
          }
        }
        """;

    private const string PlainProcedure = """
        {
          "lexicon": 1,
          "id": "com.example.plain",
          "defs": {
            "main": {
              "type": "procedure",
              "input": {
                "encoding": "application/json",
                "schema": { "type": "object", "properties": { "name": { "type": "string" } } }
              },
              "output": {
                "encoding": "application/json",
                "schema": { "type": "object", "properties": { "ok": { "type": "boolean" } } }
              }
            }
          }
        }
        """;

    private const string PlainQuery = """
        {
          "lexicon": 1,
          "id": "com.example.getThing",
          "defs": {
            "main": {
              "type": "query",
              "parameters": { "type": "params", "properties": { "id": { "type": "string" } } },
              "output": {
                "encoding": "application/json",
                "schema": { "type": "object", "properties": { "name": { "type": "string" } } }
              }
            }
          }
        }
        """;

    private const string ChatUpload = """
        {
          "lexicon": 1,
          "id": "chat.bsky.example.upload",
          "defs": {
            "main": {
              "type": "procedure",
              "input": { "encoding": "image/png" }
            }
          }
        }
        """;

    private static readonly Lazy<GeneratorTestHarness.GeneratorRun> Run = new(() => GeneratorTestHarness.Run(new[]
    {
        UploadBlob, UploadPart, UploadVideo, VideoDefs, GetBlob, GetRepo, JsonProcedures, PlainProcedure, PlainQuery, ChatUpload,
    }));

    private static string Extensions => Normalize(Run.Value.Sources["ATProtoExtensions.g.cs"]);

    [Fact]
    public void GeneratedCode_Compiles()
    {
        GeneratorTestHarness.AssertCompiles(Run.Value);
    }

    [Fact]
    public void UploadBlob_WildcardInput_TakesStreamWithOctetStreamDefault()
    {
        Assert.Contains(
            "public static async System.Threading.Tasks.Task<ComAtproto.Repo.UploadBlobOutput> ComAtprotoRepoUploadBlobAsync( " +
            "this CarpaNet.IATProtoClient client, System.IO.Stream body, string contentType = \"application/octet-stream\", " +
            "System.Threading.CancellationToken cancellationToken = default)",
            Extensions);
        Assert.Contains(
            "return await global::CarpaNet.ATProtoClientXrpcExtensions.PostBinaryAsync<ComAtproto.Repo.UploadBlobOutput>( " +
            "client, \"com.atproto.repo.uploadBlob\", null, null, body, contentType, cancellationToken);",
            Extensions);
    }

    [Fact]
    public void UploadVideo_ConcreteEncoding_IsDefaultContentType()
    {
        Assert.Contains(
            "AppBskyVideoUploadVideoAsync( this CarpaNet.IATProtoClient client, System.IO.Stream body, string contentType = \"video/mp4\", " +
            "System.Threading.CancellationToken cancellationToken = default)",
            Extensions);
    }

    [Fact]
    public void UploadPart_BinaryInputWithParameters_PassesQueryParameters()
    {
        Assert.Contains(
            "public static async System.Threading.Tasks.Task<AppBsky.Video.UploadPartOutput> AppBskyVideoUploadPartAsync( " +
            "this CarpaNet.IATProtoClient client, System.IO.Stream body, string contentType = \"application/octet-stream\", " +
            "AppBsky.Video.UploadPartParameters? parameters = null, System.Threading.CancellationToken cancellationToken = default)",
            Extensions);
        Assert.Contains(
            "PostBinaryAsync<AppBsky.Video.UploadPartOutput>( client, \"app.bsky.video.uploadPart\", null, " +
            "parameters?.ToQueryParameters(), body, contentType, cancellationToken);",
            Extensions);

        // Procedures now get a Parameters class with ToQueryParameters()
        var source = Run.Value.AllSource;
        Assert.Contains("public partial class UploadPartParameters", source);
        Assert.Contains("(\"partNumber\", PartNumber.ToString())", source);
    }

    [Fact]
    public void JsonProcedureWithParameters_UsesPostWithParameters()
    {
        Assert.Contains(
            "ComExampleDoThingAsync( this CarpaNet.IATProtoClient client, ComExample.DoThingInput input, " +
            "ComExample.DoThingParameters? parameters = null, System.Threading.CancellationToken cancellationToken = default)",
            Extensions);
        Assert.Contains(
            "return await global::CarpaNet.ATProtoClientXrpcExtensions.PostWithParametersAsync<ComExample.DoThingInput, ComExample.DoThingOutput>( " +
            "client, \"com.example.doThing\", null, parameters?.ToQueryParameters(), input, cancellationToken);",
            Extensions);
    }

    [Fact]
    public void JsonProcedureWithoutParameters_IsUnchanged()
    {
        Assert.Contains(
            "ComExamplePlainAsync( this CarpaNet.IATProtoClient client, ComExample.PlainInput input, " +
            "System.Threading.CancellationToken cancellationToken = default) { " +
            "return await client.PostAsync<ComExample.PlainInput, ComExample.PlainOutput>( \"com.example.plain\", input, cancellationToken); }",
            Extensions);
    }

    [Fact]
    public void BinaryProcedure_ForProxiedService_PassesProxyDid()
    {
        Assert.Contains(
            "ChatBskyExampleUploadAsync( this CarpaNet.IATProtoClient client, System.IO.Stream body, string contentType = \"image/png\", " +
            "System.Threading.CancellationToken cancellationToken = default)",
            Extensions);
        Assert.Contains(
            "PostBinaryAsync<object>( client, \"chat.bsky.example.upload\", CarpaNet.BlueskyServices.ChatServiceDid, null, body, contentType, cancellationToken);",
            Extensions);
    }

    [Theory]
    [InlineData("ComAtprotoSyncGetBlobAsync", "ComAtproto.Sync.GetBlobParameters", "com.atproto.sync.getBlob")]
    [InlineData("ComAtprotoSyncGetRepoAsync", "ComAtproto.Sync.GetRepoParameters", "com.atproto.sync.getRepo")]
    public void BinaryOutputQuery_ReturnsBytes(string methodName, string parametersType, string nsid)
    {
        Assert.Contains(
            $"public static async System.Threading.Tasks.Task<byte[]> {methodName}( this CarpaNet.IATProtoClient client, " +
            $"{parametersType}? parameters = null, System.Threading.CancellationToken cancellationToken = default)",
            Extensions);
        Assert.Contains(
            $"return await global::CarpaNet.ATProtoClientXrpcExtensions.GetBytesAsync( client, \"{nsid}\", null, parameters?.ToQueryParameters(), cancellationToken);",
            Extensions);
    }

    [Fact]
    public void JsonQuery_IsUnchanged()
    {
        Assert.Contains(
            "return await client.GetAsync<ComExample.GetThingOutput>( \"com.example.getThing\", parameters?.ToQueryParameters(), cancellationToken);",
            Extensions);
    }

    [Fact]
    public void GeneratedMethods_AreCallableFromConsumerCode()
    {
        const string usage = """
            using System.IO;
            using System.Threading.Tasks;
            using CarpaNet;

            internal static class Usage
            {
                public static async Task CallAll(IATProtoClient client, Stream stream)
                {
                    ComAtproto.Repo.UploadBlobOutput blob = await client.ComAtprotoRepoUploadBlobAsync(stream);
                    blob = await client.ComAtprotoRepoUploadBlobAsync(stream, "image/jpeg");
                    AppBsky.Video.UploadVideoOutput video = await client.AppBskyVideoUploadVideoAsync(stream);
                    AppBsky.Video.UploadPartOutput part = await client.AppBskyVideoUploadPartAsync(
                        stream,
                        parameters: new AppBsky.Video.UploadPartParameters { JobId = "job", PartNumber = 1 });
                    byte[] bytes = await client.ComAtprotoSyncGetBlobAsync(new ComAtproto.Sync.GetBlobParameters { Did = default, Cid = "bafy" });
                    byte[] car = await client.ComAtprotoSyncGetRepoAsync();
                    ComExample.DoThingOutput done = await client.ComExampleDoThingAsync(
                        new ComExample.DoThingInput { Name = "n" },
                        new ComExample.DoThingParameters { Mode = "m", DryRun = true });
                }
            }
            """;

        var compilation = Run.Value.Compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(usage, new CSharpParseOptions(LanguageVersion.Latest)));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();

        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void XrpcEndpoints_StillGenerateForBinaryEndpoints()
    {
        var run = GeneratorTestHarness.Run(
            new[] { UploadBlob, UploadPart, GetBlob },
            new Dictionary<string, string> { ["CarpaNet_EmitXrpcEndpoints"] = "true" });

        Assert.DoesNotContain(run.GeneratorDiagnostics, d => d.Id == "ATPG001");
        var controllers = run.Sources["XrpcControllers.g.cs"];
        Assert.Contains("HttpPost(\"/xrpc/com.atproto.repo.uploadBlob\")", controllers);
        Assert.Contains("HttpPost(\"/xrpc/app.bsky.video.uploadPart\")", controllers);
        Assert.Contains("HttpGet(\"/xrpc/com.atproto.sync.getBlob\")", controllers);
    }

    [Fact]
    public async Task GeneratedMethods_RunThroughTheClientPipeline()
    {
        const string driver = """
            using System.IO;
            using System.Text.Json;
            using System.Threading.Tasks;
            using CarpaNet;

            public static class Driver
            {
                public static JsonSerializerOptions Options => CarpaNet.Json.ATProtoJsonContext.DefaultOptions;

                public static async Task<string> Run(IATProtoClient client)
                {
                    var blob = await client.ComAtprotoRepoUploadBlobAsync(new MemoryStream(new byte[] { 1, 2, 3 }), "image/png");
                    var part = await client.AppBskyVideoUploadPartAsync(
                        new MemoryStream(new byte[] { 4, 5 }),
                        parameters: new AppBsky.Video.UploadPartParameters { JobId = "job1", PartNumber = 2 });
                    var bytes = await client.ComAtprotoSyncGetBlobAsync(
                        new ComAtproto.Sync.GetBlobParameters { Did = new ATDid("did:plc:user"), Cid = "bafy" });
                    return blob.Blob.Ref + "|" + blob.Blob.MimeType + "|" + part.PartNumber + "|" + bytes.Length;
                }
            }
            """;

        var run = Run.Value;
        var withDriver = new GeneratorTestHarness.GeneratorRun
        {
            Compilation = run.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(driver, new CSharpParseOptions(LanguageVersion.Latest))),
            GeneratorDiagnostics = run.GeneratorDiagnostics,
            Sources = run.Sources,
        };
        var driverType = GeneratorTestHarness.CompileAndLoad(withDriver).GetType("Driver")!;

        var handler = new CarpaNet.UnitTests.Http.RecordingHandler(r => r.Uri.AbsolutePath switch
        {
            "/xrpc/com.atproto.repo.uploadBlob" => CarpaNet.UnitTests.Http.XrpcRequestPipelineTests.Json(
                "{\"blob\":{\"$type\":\"blob\",\"ref\":{\"$link\":\"bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy\"},\"mimeType\":\"image/png\",\"size\":3}}"),
            "/xrpc/app.bsky.video.uploadPart" => CarpaNet.UnitTests.Http.XrpcRequestPipelineTests.Json("{\"partNumber\":2,\"sizeBytes\":2}"),
            _ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.ByteArrayContent(new byte[] { 9, 9, 9, 9 }) },
        });
        var options = CarpaNet.UnitTests.Http.XrpcRequestPipelineTests.CreateOptions(new System.Net.Http.HttpClient(handler));
        options.JsonOptions = (System.Text.Json.JsonSerializerOptions)driverType.GetProperty("Options")!.GetValue(null)!;
        using var client = CarpaNet.ATProtoClient.CreateWithRestoredSession(
            CarpaNet.UnitTests.Http.XrpcRequestPipelineTests.CreateJwt("did:plc:user", DateTimeOffset.UtcNow.AddHours(1), "a"),
            CarpaNet.UnitTests.Http.XrpcRequestPipelineTests.CreateJwt("did:plc:user", DateTimeOffset.UtcNow.AddDays(1), "r"),
            "did:plc:user", "user.example", new Uri("https://pds.user.example"), options);

        var result = await (Task<string>)driverType.GetMethod("Run")!.Invoke(null, new object[] { client })!;

        Assert.Equal("bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy|image/png|2|4", result);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("image/png", handler.Requests[0].ContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, handler.Requests[0].Body);
        Assert.Equal("application/octet-stream", handler.Requests[1].ContentType);
        Assert.Equal("?jobId=job1&partNumber=2", handler.Requests[1].Uri.Query);
        Assert.Equal("*/*", handler.Requests[2].Header("Accept"));
        Assert.All(handler.Requests, r => Assert.StartsWith("Bearer ", r.Header("Authorization")));
    }

    private static string Normalize(string source) => Regex.Replace(source, @"\s+", " ");
}
