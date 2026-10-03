using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Identity;
using Xunit;

namespace CarpaNet.UnitTests.Identity;

/// <summary>
/// HttpMessageHandler that answers requests with a delegate and records them. No network access.
/// </summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = (request, _) => Task.FromResult(respond(request));
    }

    public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return _respond(request, cancellationToken);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    public static HttpResponseMessage Text(string text, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain"),
        };
    }
}

public class DnsOverHttpsResolverTests
{
    private const string Endpoint1 = "https://doh1.test/dns-query";
    private const string Endpoint2 = "https://doh2.test/resolve";

    private static string TxtResponse(params string[] data)
    {
        var answers = string.Join(",", data.Select(d =>
            $"{{\"name\":\"_atproto.example.com\",\"type\":16,\"TTL\":300,\"data\":{System.Text.Json.JsonSerializer.Serialize(d)}}}"));
        return $"{{\"Status\":0,\"TC\":false,\"RD\":true,\"RA\":true,\"AD\":false,\"CD\":false,\"Answer\":[{answers}]}}";
    }

    private static (DnsOverHttpsResolver Resolver, FakeHttpHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, TimeSpan? timeout = null)
    {
        var handler = new FakeHttpHandler(respond);
        var resolver = new DnsOverHttpsResolver(new HttpClient(handler), new[] { Endpoint1, Endpoint2 }, timeout);
        return (resolver, handler);
    }

    [Fact]
    public async Task GetTxtRecords_SendsJsonQuery()
    {
        var (resolver, handler) = Create(_ => FakeHttpHandler.Json(TxtResponse("\"did=did:plc:abc\"")));

        await resolver.GetTxtRecordsAsync("_atproto.example.com");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://doh1.test/dns-query?name=_atproto.example.com&type=TXT", request.RequestUri!.ToString());
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/dns-json");
    }

    [Fact]
    public async Task GetTxtRecords_SingleQuotedTxt_IsUnquoted()
    {
        var (resolver, _) = Create(_ => FakeHttpHandler.Json(TxtResponse("\"did=did:plc:abc123\"")));

        var records = await resolver.GetTxtRecordsAsync("_atproto.example.com");

        Assert.Equal(new[] { "did=did:plc:abc123" }, records);
    }

    [Fact]
    public async Task GetTxtRecords_UnquotedTxt_IsReturnedAsIs()
    {
        var (resolver, _) = Create(_ => FakeHttpHandler.Json(TxtResponse("did=did:plc:abc123")));

        var records = await resolver.GetTxtRecordsAsync("_atproto.example.com");

        Assert.Equal(new[] { "did=did:plc:abc123" }, records);
    }

    [Fact]
    public async Task GetTxtRecords_SplitCharacterStrings_AreJoined()
    {
        var (resolver, _) = Create(_ => FakeHttpHandler.Json(TxtResponse("\"did=did:plc:\" \"abc123\"")));

        var records = await resolver.GetTxtRecordsAsync("_atproto.example.com");

        Assert.Equal(new[] { "did=did:plc:abc123" }, records);
    }

    [Fact]
    public async Task GetTxtRecords_MultipleAnswers_IgnoresNonTxt()
    {
        const string json = "{\"Status\":0,\"Answer\":[" +
            "{\"name\":\"_atproto.example.com\",\"type\":5,\"TTL\":300,\"data\":\"target.example.com.\"}," +
            "{\"name\":\"target.example.com\",\"type\":16,\"TTL\":300,\"data\":\"\\\"v=spf1 -all\\\"\"}," +
            "{\"name\":\"target.example.com\",\"type\":16,\"TTL\":300,\"data\":\"\\\"did=did:plc:xyz\\\"\"}]}";
        var (resolver, _) = Create(_ => FakeHttpHandler.Json(json));

        var records = await resolver.GetTxtRecordsAsync("_atproto.example.com");

        Assert.Equal(new[] { "v=spf1 -all", "did=did:plc:xyz" }, records);
    }

    [Fact]
    public async Task GetTxtRecords_NoErrorWithoutAnswer_ReturnsEmptyWithoutFallback()
    {
        var (resolver, handler) = Create(_ => FakeHttpHandler.Json("{\"Status\":0}"));

        var records = await resolver.GetTxtRecordsAsync("_atproto.example.com");

        Assert.Empty(records);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetTxtRecords_NxDomain_ReturnsEmptyWithoutFallback()
    {
        var (resolver, handler) = Create(_ => FakeHttpHandler.Json("{\"Status\":3,\"Authority\":[]}"));

        var records = await resolver.GetTxtRecordsAsync("_atproto.example.com");

        Assert.Empty(records);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetTxtRecords_ServFail_FallsBackToNextEndpoint()
    {
        var (resolver, handler) = Create(request =>
            request.RequestUri!.Host == "doh1.test"
                ? FakeHttpHandler.Json("{\"Status\":2}")
                : FakeHttpHandler.Json(TxtResponse("\"did=did:plc:fallback\"")));

        var records = await resolver.GetTxtRecordsAsync("_atproto.example.com");

        Assert.Equal(new[] { "did=did:plc:fallback" }, records);
        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith(Endpoint2, handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetTxtRecords_HttpError_FallsBackToNextEndpoint()
    {
        var (resolver, _) = Create(request =>
            request.RequestUri!.Host == "doh1.test"
                ? FakeHttpHandler.Text("bad gateway", HttpStatusCode.BadGateway)
                : FakeHttpHandler.Json(TxtResponse("\"did=did:plc:fallback\"")));

        var records = await resolver.GetTxtRecordsAsync("_atproto.example.com");

        Assert.Equal(new[] { "did=did:plc:fallback" }, records);
    }

    [Fact]
    public async Task GetTxtRecords_MalformedJson_FallsBackToNextEndpoint()
    {
        var (resolver, _) = Create(request =>
            request.RequestUri!.Host == "doh1.test"
                ? FakeHttpHandler.Json("{\"Status\":0,\"Answer\":[{")
                : FakeHttpHandler.Json(TxtResponse("\"did=did:plc:fallback\"")));

        var records = await resolver.GetTxtRecordsAsync("_atproto.example.com");

        Assert.Equal(new[] { "did=did:plc:fallback" }, records);
    }

    [Fact]
    public async Task GetTxtRecords_TransportError_FallsBackToNextEndpoint()
    {
        var (resolver, _) = Create(request =>
            request.RequestUri!.Host == "doh1.test"
                ? throw new HttpRequestException("connection refused")
                : FakeHttpHandler.Json(TxtResponse("\"did=did:plc:fallback\"")));

        var records = await resolver.GetTxtRecordsAsync("_atproto.example.com");

        Assert.Equal(new[] { "did=did:plc:fallback" }, records);
    }

    [Fact]
    public async Task GetTxtRecords_AllEndpointsFail_ReturnsEmpty()
    {
        var (resolver, handler) = Create(_ => FakeHttpHandler.Json("not json"));

        var records = await resolver.GetTxtRecordsAsync("_atproto.example.com");

        Assert.Empty(records);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetTxtRecords_EndpointTimeout_FallsBackToNextEndpoint()
    {
        var handler = new FakeHttpHandler(async (request, ct) =>
        {
            if (request.RequestUri!.Host == "doh1.test")
                await Task.Delay(Timeout.Infinite, ct);
            return FakeHttpHandler.Json(TxtResponse("\"did=did:plc:fallback\""));
        });
        var resolver = new DnsOverHttpsResolver(new HttpClient(handler), new[] { Endpoint1, Endpoint2 }, TimeSpan.FromMilliseconds(50));

        var records = await resolver.GetTxtRecordsAsync("_atproto.example.com");

        Assert.Equal(new[] { "did=did:plc:fallback" }, records);
    }

    [Fact]
    public async Task GetTxtRecords_CallerCancellation_Throws()
    {
        var (resolver, _) = Create(_ => FakeHttpHandler.Json(TxtResponse("\"did=did:plc:abc\"")));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.GetTxtRecordsAsync("_atproto.example.com", cts.Token));
    }

    [Fact]
    public void Constructor_NoEndpoints_UsesDefaults()
    {
        var resolver = new DnsOverHttpsResolver(new HttpClient(new FakeHttpHandler(_ => FakeHttpHandler.Text(""))));

        Assert.Equal(DnsOverHttpsResolver.DefaultEndpoints, resolver.Endpoints);
        Assert.Equal(DnsOverHttpsResolver.CloudflareEndpoint, resolver.Endpoints[0]);
    }

    [Fact]
    public void Constructor_NullHttpClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DnsOverHttpsResolver(null!));
    }

    [Theory]
    [InlineData("\"did=did:plc:abc\"", "did=did:plc:abc")]
    [InlineData("did=did:plc:abc", "did=did:plc:abc")]
    [InlineData("\"a\" \"b\" \"c\"", "abc")]
    [InlineData("\"say \\\"hi\\\"\"", "say \"hi\"")]
    [InlineData("\"a\\059b\"", "a;b")]
    [InlineData("\"\"", "")]
    public void ParseTxtData_HandlesPresentationFormat(string data, string expected)
    {
        Assert.Equal(expected, DnsOverHttpsResolver.ParseTxtData(data));
    }
}

public class DnsResolverDefaultsTests
{
    [Fact]
    public void CreateDefault_OnNonBrowserHost_ReturnsUdpResolver()
    {
        // Unit tests run on a desktop/server runtime, which supports UDP.
        Assert.True(DnsResolverDefaults.IsUdpDnsSupported);

        var resolver = DnsResolverDefaults.CreateDefault(new HttpClient(new FakeHttpHandler(_ => FakeHttpHandler.Text(""))));

        Assert.IsType<DefaultDnsResolver>(resolver);
    }

    [Fact]
    public void CreateDefault_NullHttpClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DnsResolverDefaults.CreateDefault(null!));
    }

    [Fact]
    public void IdentityResolver_WithoutDnsResolver_UsesPlatformDefault()
    {
        using var resolver = new IdentityResolver(new HttpClient(new FakeHttpHandler(_ => FakeHttpHandler.Text(""))));

        Assert.IsType<DefaultDnsResolver>(resolver.DnsResolver);
    }
}
