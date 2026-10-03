using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Http;
using CarpaNet.Identity;
using Xunit;

namespace CarpaNet.UnitTests.Http;

/// <summary>
/// Tests for the shared XRPC send pipeline: credential scoping, repo routing, per-request options,
/// binary bodies and responses, and body replay on token refresh.
/// </summary>
public class XrpcRequestPipelineTests
{
    private const string UserDid = "did:plc:user";
    private const string OtherDid = "did:plc:other";
    private static readonly Uri UserPds = new("https://pds.user.example");
    private static readonly Uri OtherPds = new("https://pds.other.example");

    [Fact]
    public async Task Get_OwnPds_AttachesBearerToken()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":1}"));
        using var client = CreateSessionClient(handler, out var accessJwt);

        await client.GetAsync<JsonElement>("com.example.get");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(UserPds.Host, request.Uri.Host);
        Assert.Equal("Bearer " + accessJwt, request.Header("Authorization"));
    }

    [Fact]
    public async Task Get_ForeignRepo_RoutesToOwnerPdsWithoutCredentials()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":1}"));
        using var client = CreateSessionClient(handler, out _, await CreateResolverAsync(handler));

        await client.GetAsync<JsonElement>("com.atproto.repo.getRecord", Params(("repo", OtherDid), ("collection", "app.bsky.feed.post"), ("rkey", "abc")));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(OtherPds.Host, request.Uri.Host);
        Assert.Null(request.Header("Authorization"));
        Assert.Null(request.Header("DPoP"));
    }

    [Fact]
    public async Task Get_OwnRepo_KeepsCredentials()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":1}"));
        using var client = CreateSessionClient(handler, out var accessJwt, await CreateResolverAsync(handler));

        await client.GetAsync<JsonElement>("com.atproto.repo.getRecord", Params(("repo", UserDid)));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(UserPds.Host, request.Uri.Host);
        Assert.Equal("Bearer " + accessJwt, request.Header("Authorization"));
    }

    [Fact]
    public async Task Get_WithProxy_IsNotReroutedByRepo()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":1}"));
        using var client = CreateSessionClient(handler, out _, await CreateResolverAsync(handler));

        await client.GetAsync<JsonElement>("app.bsky.example.get", "did:web:api.bsky.app#bsky_appview", Params(("repo", OtherDid)));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(UserPds.Host, request.Uri.Host);
        Assert.Equal("did:web:api.bsky.app#bsky_appview", request.Header("atproto-proxy"));
        Assert.NotNull(request.Header("Authorization"));
    }

    [Fact]
    public async Task ServiceUrl_SendsNoSessionCredentials_ButKeepsCallerAuthorization()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":1}"));
        using var client = CreateSessionClient(handler, out _);
        var video = client
            .WithServiceUrl(new Uri("https://video.example"))
            .WithHeader("Authorization", "Bearer service-auth-token");

        await video.GetAsync<JsonElement>("app.bsky.video.getUploadLimits");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("video.example", request.Uri.Host);
        Assert.Equal("Bearer service-auth-token", request.Header("Authorization"));
    }

    [Fact]
    public async Task ServiceUrl_WithoutCallerAuthorization_SendsNone()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":1}"));
        using var client = CreateSessionClient(handler, out _);

        await client.WithServiceUrl(new Uri("https://public.api.example")).GetAsync<JsonElement>("app.bsky.feed.getFeed");

        Assert.Null(Assert.Single(handler.Requests).Header("Authorization"));
    }

    [Fact]
    public async Task WithoutProxy_OverridesGeneratedProxy()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":1}"));
        using var client = CreateSessionClient(handler, out _);

        // Generated chat methods call the proxy overload.
        await client.WithoutProxy().GetAsync<JsonElement>("chat.bsky.convo.getLog", BlueskyServices.ChatServiceDid);

        Assert.Null(Assert.Single(handler.Requests).Header("atproto-proxy"));
    }

    [Fact]
    public async Task WithProxy_AppliesToEveryCall()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":1}"));
        using var client = CreateSessionClient(handler, out _);
        var appview = client.WithProxy("did:web:api.bsky.app#bsky_appview");

        await appview.GetAsync<JsonElement>("app.bsky.feed.getTimeline");
        await appview.PostAsync<object, JsonElement>("app.bsky.graph.muteActor", new { actor = OtherDid });

        Assert.All(handler.Requests, r => Assert.Equal("did:web:api.bsky.app#bsky_appview", r.Header("atproto-proxy")));
    }

    [Fact]
    public async Task NestedScopes_OuterScopeWins()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":1}"));
        using var client = CreateSessionClient(handler, out _);
        var scoped = client
            .WithProxy("did:web:inner.example#svc")
            .WithHeader("x-test", "inner")
            .WithProxy("did:web:outer.example#svc")
            .WithHeader("x-test", "outer");

        await scoped.GetAsync<JsonElement>("com.example.get");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("did:web:outer.example#svc", request.Header("atproto-proxy"));
        Assert.Equal("outer", request.Header("x-test"));
        Assert.Same(client, scoped.Inner);
    }

    [Fact]
    public async Task SetLabelerDids_ChangesHeaderOnLaterRequests_AndKeepsRedact()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":1}"));
        using var client = CreateSessionClient(handler, out _);

        await client.GetAsync<JsonElement>("com.example.get");
        client.SetLabelerDids(new[] { AcceptLabelersHeader.Redact("did:plc:mod"), "did:plc:custom" });
        await client.GetAsync<JsonElement>("com.example.get");

        Assert.Null(handler.Requests[0].Header("atproto-accept-labelers"));
        Assert.Equal("did:plc:mod;redact,did:plc:custom", handler.Requests[1].Header("atproto-accept-labelers"));
    }

    [Fact]
    public async Task WithAcceptLabelers_ReplacesClientList()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":1}"));
        using var client = CreateSessionClient(handler, out _);
        client.SetLabelerDids(new[] { "did:plc:client" });

        var scoped = client.WithAcceptLabelers(new[] { "did:plc:scoped" });
        await scoped.GetAsync<JsonElement>("com.example.get");

        Assert.Equal("did:plc:scoped", Assert.Single(handler.Requests).Header("atproto-accept-labelers"));
        Assert.Equal(new[] { "did:plc:scoped" }, scoped.LabelerDids);
    }

    [Fact]
    public async Task PostBinaryAsync_SendsStreamWithContentTypeAndParameters()
    {
        var handler = new RecordingHandler(_ => Json("{\"ok\":true}"));
        using var client = CreateSessionClient(handler, out _);
        var data = Encoding.UTF8.GetBytes("part-bytes");

        var result = await client.PostBinaryAsync<JsonElement>(
            "app.bsky.video.uploadPart", null, Params(("jobId", "job1"), ("partNumber", "2")),
            new MemoryStream(data), "application/octet-stream");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("?jobId=job1&partNumber=2", request.Uri.Query);
        Assert.Equal("application/octet-stream", request.ContentType);
        Assert.Equal(data, request.Body);
        Assert.True(result.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task PostBinaryAsync_SeekableStream_IsReplayedAfterTokenRefresh()
    {
        var calls = 0;
        var handler = new RecordingHandler(r =>
        {
            if (r.Uri.AbsolutePath.EndsWith("refreshSession", StringComparison.Ordinal))
            {
                return Json(SessionJson(CreateJwt(UserDid, DateTimeOffset.UtcNow.AddHours(2), "fresh")));
            }

            return ++calls == 1 ? Status(HttpStatusCode.Unauthorized) : Json("{\"ok\":true}");
        });
        using var client = CreateSessionClient(handler, out _);
        var data = Encoding.UTF8.GetBytes("blob-data");

        await client.PostBinaryAsync<JsonElement>("com.atproto.repo.uploadBlob", null, null, new MemoryStream(data), "image/png");

        var uploads = handler.Requests.Where(r => r.Uri.AbsolutePath.EndsWith("uploadBlob", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, uploads.Count);
        Assert.Equal(data, uploads[0].Body);
        Assert.Equal(data, uploads[1].Body);
    }

    [Fact]
    public async Task PostBinaryAsync_NonSeekableStream_IsNotRetried()
    {
        var handler = new RecordingHandler(_ => Status(HttpStatusCode.Unauthorized));
        using var client = CreateSessionClient(handler, out _);

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            client.PostBinaryAsync<JsonElement>("com.atproto.repo.uploadBlob", null, null, new NonSeekableStream(new byte[] { 1, 2, 3 }), "image/png"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetBytesAsync_ReturnsBodyAndAcceptsAnyType()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var client = CreateSessionClient(handler, out _);

        var result = await client.GetBytesAsync("com.atproto.sync.getBlob", null, Params(("did", UserDid), ("cid", "bafy")));

        Assert.Equal(bytes, result);
        Assert.Equal("*/*", Assert.Single(handler.Requests).Header("Accept"));
    }

    [Fact]
    public async Task GetBytesAsync_ErrorStatus_Throws()
    {
        var handler = new RecordingHandler(_ => Json("{\"error\":\"BlobNotFound\",\"message\":\"nope\"}", HttpStatusCode.BadRequest));
        using var client = CreateSessionClient(handler, out _);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => client.GetBytesAsync("com.atproto.sync.getBlob", null, null));
        Assert.Equal("BlobNotFound", ex.ErrorCode);
    }

    [Fact]
    public async Task PostWithParametersAsync_SendsParametersAndJsonBody()
    {
        var handler = new RecordingHandler(_ => Json("{\"ok\":true}"));
        using var client = CreateSessionClient(handler, out _);

        await client.PostWithParametersAsync<object, JsonElement>(
            "com.example.procedure", "did:web:svc.example#svc", Params(("mode", "fast")), new { name = "x" });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("?mode=fast", request.Uri.Query);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("{\"name\":\"x\"}", Encoding.UTF8.GetString(request.Body!));
        Assert.Equal("did:web:svc.example#svc", request.Header("atproto-proxy"));
    }

    [Fact]
    public async Task UserAgent_IsAddedPerRequest_WhenCallerSuppliesHttpClient()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":1}"));
        var options = CreateOptions(new HttpClient(handler));
        options.UserAgent = "CarpaNetTests/1.0";
        using var client = ATProtoClient.Create(options);

        await client.GetAsync<JsonElement>("com.example.get");

        Assert.Equal("CarpaNetTests/1.0", Assert.Single(handler.Requests).Header("User-Agent"));
    }

    [Fact]
    public void UserAgent_IsSetOnOwnedHttpClient()
    {
        var options = CreateOptions(null);
        options.UserAgent = "CarpaNetTests/1.0";
        using var client = ATProtoClient.Create(options);

        Assert.Equal("CarpaNetTests/1.0", client.HttpClient.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public void Combine_OuterProxyAndDisableWin_HeadersMerge()
    {
        var inner = new XrpcRequestOptions
        {
            ProxyServiceDid = "did:web:inner#svc",
            AcceptLabelers = new[] { "did:plc:inner" },
            Headers = new Dictionary<string, string> { ["a"] = "inner", ["b"] = "inner" },
        };
        var outer = new XrpcRequestOptions
        {
            DisableProxy = true,
            Headers = new Dictionary<string, string> { ["A"] = "outer" },
        };

        var combined = XrpcRequestOptions.Combine(outer, inner)!;

        Assert.Null(combined.EffectiveProxyServiceDid);
        Assert.Equal(new[] { "did:plc:inner" }, combined.AcceptLabelers);
        Assert.Equal("outer", combined.Headers!["a"]);
        Assert.Equal("inner", combined.Headers!["b"]);
        Assert.Same(inner, XrpcRequestOptions.Combine(null, inner));
    }

    [Fact]
    public void Combine_OuterWithoutProxy_KeepsInnerProxy()
    {
        var combined = XrpcRequestOptions.Combine(
            new XrpcRequestOptions { Headers = new Dictionary<string, string> { ["x"] = "1" } },
            new XrpcRequestOptions { ProxyServiceDid = BlueskyServices.ChatServiceDid })!;

        Assert.Equal(BlueskyServices.ChatServiceDid, combined.EffectiveProxyServiceDid);
    }

    [Theory]
    [InlineData("https://pds.example", "https://pds.example/xrpc/a", true)]
    [InlineData("https://pds.example", "https://PDS.example:443/xrpc/a", true)]
    [InlineData("https://pds.example", "http://pds.example/xrpc/a", false)]
    [InlineData("https://pds.example", "https://pds.example:8443/xrpc/a", false)]
    [InlineData("https://pds.example", "https://evil.pds.example/xrpc/a", false)]
    public void IsSameOrigin_ComparesSchemeHostPort(string origin, string url, bool expected)
    {
        Assert.Equal(expected, XrpcHttpHandler.IsSameOrigin(new Uri(url), new Uri(origin)));
    }

    [Fact]
    public async Task ProgressReportingStream_ReportsBytesRead()
    {
        var reports = new List<long>();
        var progress = new SyncProgress(reports.Add);
        using var stream = new ProgressReportingStream(new MemoryStream(new byte[10]), progress);
        var buffer = new byte[4];

        while (await stream.ReadAsync(buffer, 0, buffer.Length) > 0)
        {
        }

        Assert.Equal(new long[] { 4, 8, 10 }, reports);
        Assert.Equal(10, stream.BytesRead);
    }

    [Fact]
    public async Task RateLimitHandler_UnreplayableBody_ReturnsRateLimitResponse()
    {
        var inner = new RecordingHandler(_ => Status((HttpStatusCode)429));
        using var rateLimit = new RateLimitHandler(inner) { AutoRetryOnRateLimit = true, MaxRetries = 3 };
        using var http = new HttpClient(rateLimit);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://pds.example/xrpc/com.atproto.repo.uploadBlob")
        {
            Content = new StreamContent(new NonSeekableStream(new byte[] { 1, 2, 3 })),
        };

        using var response = await http.SendAsync(request);

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    #region Helpers

    internal static ATProtoClientOptions CreateOptions(HttpClient? httpClient)
    {
        return new ATProtoClientOptions
        {
            HttpClient = httpClient,
            JsonOptions = TestHelpers.CreateJsonOptions(),
            CborContext = TestHelpers.CreateCborContext(),
            CreateIdentityResolver = false,
        };
    }

    internal static ATProtoClient CreateSessionClient(RecordingHandler handler, out string accessJwt, IdentityResolver? resolver = null)
    {
        accessJwt = CreateJwt(UserDid, DateTimeOffset.UtcNow.AddHours(2), "access");
        var refreshJwt = CreateJwt(UserDid, DateTimeOffset.UtcNow.AddDays(30), "refresh");
        var options = CreateOptions(new HttpClient(handler));
        options.IdentityResolver = resolver;
        return ATProtoClient.CreateWithRestoredSession(accessJwt, refreshJwt, UserDid, "user.example", UserPds, options);
    }

    private static async Task<IdentityResolver> CreateResolverAsync(RecordingHandler handler)
    {
        var cache = new MemoryIdentityCache();
        await cache.SetDidDocumentAsync(UserDid, DidDoc(UserDid, UserPds));
        await cache.SetDidDocumentAsync(OtherDid, DidDoc(OtherDid, OtherPds));
        return new IdentityResolver(new HttpClient(handler), cache: cache);
    }

    private static DidDocument DidDoc(string did, Uri pds)
    {
        return new DidDocument
        {
            Id = did,
            Service = new List<DidService>
            {
                new DidService { Id = "#atproto_pds", Type = "AtprotoPersonalDataServer", ServiceEndpoint = pds.ToString().TrimEnd('/') },
            },
        };
    }

    internal static string CreateJwt(string sub, DateTimeOffset expires, string nonce)
    {
        static string B64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return B64("{\"alg\":\"none\",\"typ\":\"JWT\"}") + "." +
               B64($"{{\"sub\":\"{sub}\",\"exp\":{expires.ToUnixTimeSeconds()},\"jti\":\"{nonce}\"}}") + ".sig";
    }

    internal static string SessionJson(string accessJwt)
    {
        var refresh = CreateJwt(UserDid, DateTimeOffset.UtcNow.AddDays(30), "refresh2");
        return $"{{\"accessJwt\":\"{accessJwt}\",\"refreshJwt\":\"{refresh}\",\"handle\":\"user.example\",\"did\":\"{UserDid}\"}}";
    }

    internal static IEnumerable<KeyValuePair<string, string>> Params(params (string Key, string Value)[] values)
        => values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).ToList();

    internal static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    internal static HttpResponseMessage Status(HttpStatusCode status)
        => new(status) { Content = new StringContent(string.Empty) };

    #endregion
}

/// <summary>
/// Records each request (with its body read at send time) and answers through a callback.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, HttpResponseMessage> _respond;

    public RecordingHandler(Func<RecordedRequest, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public List<RecordedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[]? body = null;
        string? contentType = null;
        if (request.Content != null)
        {
            // Copy without buffering, as a socket handler does, so an unreplayable body stays unreplayable.
            using var copy = new MemoryStream();
            await request.Content.CopyToAsync(copy, cancellationToken);
            body = copy.ToArray();
            contentType = request.Content.Headers.ContentType?.MediaType;
        }

        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, headers, body, contentType);
        Requests.Add(recorded);
        return _respond(recorded);
    }
}

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    Dictionary<string, string> Headers,
    byte[]? Body,
    string? ContentType)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

internal sealed class NonSeekableStream : MemoryStream
{
    public NonSeekableStream(byte[] data)
        : base(data)
    {
    }

    public override bool CanSeek => false;
}

internal sealed class SyncProgress : IProgress<long>
{
    private readonly Action<long> _report;

    public SyncProgress(Action<long> report)
    {
        _report = report;
    }

    public void Report(long value) => _report(value);
}
