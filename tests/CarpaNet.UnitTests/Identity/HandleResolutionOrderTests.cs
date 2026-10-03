using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Identity;
using Xunit;

namespace CarpaNet.UnitTests.Identity;

public class HandleResolutionOrderTests
{
    private const string Handle = "alice.example.com";
    private const string Service = "https://appview.test";

    private sealed class FakeDnsResolver : IDnsResolver
    {
        private readonly Func<string, IReadOnlyList<string>> _answer;

        public FakeDnsResolver(Func<string, IReadOnlyList<string>> answer) => _answer = answer;

        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_answer(name));
        }
    }

    private static FakeDnsResolver NoDns() => new(_ => Array.Empty<string>());

    private static bool IsWellKnown(HttpRequestMessage r) => r.RequestUri!.AbsolutePath == "/.well-known/atproto-did";

    private static bool IsXrpc(HttpRequestMessage r) => r.RequestUri!.AbsolutePath == "/xrpc/com.atproto.identity.resolveHandle";

    [Fact]
    public async Task DefaultOrder_DnsWins_NoHttpRequests()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Text("", HttpStatusCode.NotFound));
        var dns = new FakeDnsResolver(_ => new[] { "did=did:plc:fromdns" });
        using var resolver = new IdentityResolver(new HttpClient(handler), new IdentityResolverOptions
        {
            DnsResolver = dns,
            HandleResolutionServiceUrl = Service,
        });

        var did = await resolver.ResolveHandleAsync(Handle);

        Assert.Equal("did:plc:fromdns", did);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DefaultOrder_WellKnownBeforeXrpc()
    {
        var handler = new FakeHttpHandler(r =>
            IsWellKnown(r) ? FakeHttpHandler.Text("did:plc:fromwellknown\n")
            : FakeHttpHandler.Json("{\"did\":\"did:plc:fromxrpc\"}"));
        using var resolver = new IdentityResolver(new HttpClient(handler), new IdentityResolverOptions
        {
            DnsResolver = NoDns(),
            HandleResolutionServiceUrl = Service,
        });

        var did = await resolver.ResolveHandleAsync(Handle);

        Assert.Equal("did:plc:fromwellknown", did);
        Assert.DoesNotContain(handler.Requests, IsXrpc);
    }

    [Fact]
    public async Task DefaultOrder_FallsBackToXrpc()
    {
        var handler = new FakeHttpHandler(r =>
            IsWellKnown(r) ? FakeHttpHandler.Text("", HttpStatusCode.NotFound)
            : FakeHttpHandler.Json("{\"did\":\"did:plc:fromxrpc\"}"));
        using var resolver = new IdentityResolver(new HttpClient(handler), new IdentityResolverOptions
        {
            DnsResolver = NoDns(),
            HandleResolutionServiceUrl = Service + "/",
        });

        var did = await resolver.ResolveHandleAsync(Handle);

        Assert.Equal("did:plc:fromxrpc", did);
        var xrpc = Assert.Single(handler.Requests, IsXrpc);
        Assert.Equal("https://appview.test/xrpc/com.atproto.identity.resolveHandle?handle=alice.example.com", xrpc.RequestUri!.ToString());
    }

    [Fact]
    public async Task XrpcOnly_SkipsDnsAndWellKnown()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("{\"did\":\"did:plc:fromxrpc\"}"));
        var dns = new FakeDnsResolver(_ => new[] { "did=did:plc:fromdns" });
        using var resolver = new IdentityResolver(new HttpClient(handler), new IdentityResolverOptions
        {
            DnsResolver = dns,
            HandleResolutionServiceUrl = Service,
            HandleResolutionOrder = new[] { HandleResolutionMethod.Xrpc },
        });

        var did = await resolver.ResolveHandleAsync(Handle);

        Assert.Equal("did:plc:fromxrpc", did);
        Assert.Equal(0, dns.Calls);
        Assert.All(handler.Requests, r => Assert.True(IsXrpc(r)));
    }

    [Fact]
    public async Task CustomOrder_XrpcBeforeDns()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("{\"error\":\"InvalidRequest\",\"message\":\"Unable to resolve handle\"}", HttpStatusCode.BadRequest));
        var dns = new FakeDnsResolver(_ => new[] { "did=did:plc:fromdns" });
        using var resolver = new IdentityResolver(new HttpClient(handler), new IdentityResolverOptions
        {
            DnsResolver = dns,
            HandleResolutionServiceUrl = Service,
            HandleResolutionOrder = new[] { HandleResolutionMethod.Xrpc, HandleResolutionMethod.Dns },
        });

        var did = await resolver.ResolveHandleAsync(Handle);

        Assert.Equal("did:plc:fromdns", did);
        Assert.Single(handler.Requests, IsXrpc);
        Assert.Equal(1, dns.Calls);
    }

    [Fact]
    public async Task Xrpc_WithoutServiceUrl_IsSkipped()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Text("", HttpStatusCode.NotFound));
        using var resolver = new IdentityResolver(new HttpClient(handler), new IdentityResolverOptions
        {
            DnsResolver = NoDns(),
        });

        await Assert.ThrowsAsync<IdentityResolutionException>(() => resolver.ResolveHandleAsync(Handle));
        Assert.DoesNotContain(handler.Requests, IsXrpc);
    }

    [Theory]
    [InlineData("{\"did\":\"not-a-did\"}", HttpStatusCode.OK)]
    [InlineData("{\"did\":", HttpStatusCode.OK)]
    [InlineData("{}", HttpStatusCode.OK)]
    [InlineData("{\"error\":\"InvalidRequest\"}", HttpStatusCode.BadRequest)]
    [InlineData("oops", HttpStatusCode.InternalServerError)]
    public async Task Xrpc_BadResponse_FailsResolution(string body, HttpStatusCode status)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(body, status));
        using var resolver = new IdentityResolver(new HttpClient(handler), new IdentityResolverOptions
        {
            HandleResolutionServiceUrl = Service,
            HandleResolutionOrder = new[] { HandleResolutionMethod.Xrpc },
        });

        await Assert.ThrowsAsync<IdentityResolutionException>(() => resolver.ResolveHandleAsync(Handle));
    }

    [Fact]
    public async Task Xrpc_TransportError_FailsResolution()
    {
        var handler = new FakeHttpHandler(_ => throw new HttpRequestException("connection refused"));
        using var resolver = new IdentityResolver(new HttpClient(handler), new IdentityResolverOptions
        {
            HandleResolutionServiceUrl = Service,
            HandleResolutionOrder = new[] { HandleResolutionMethod.Xrpc },
        });

        await Assert.ThrowsAsync<IdentityResolutionException>(() => resolver.ResolveHandleAsync(Handle));
    }

    [Fact]
    public async Task Xrpc_Result_IsCached()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("{\"did\":\"did:plc:fromxrpc\"}"));
        var cache = new MemoryIdentityCache();
        using var resolver = new IdentityResolver(new HttpClient(handler), new IdentityResolverOptions
        {
            Cache = cache,
            HandleResolutionServiceUrl = Service,
            HandleResolutionOrder = new[] { HandleResolutionMethod.Xrpc },
        });

        await resolver.ResolveHandleAsync(Handle);
        var did = await resolver.ResolveHandleAsync(Handle);

        Assert.Equal("did:plc:fromxrpc", did);
        Assert.Single(handler.Requests);
        Assert.Equal("did:plc:fromxrpc", await cache.GetHandleDidAsync(Handle));
    }

    [Fact]
    public async Task ResolveAsync_XrpcResolvedHandle_StillChecksDidDocumentHandle()
    {
        const string didDoc = "{\"id\":\"did:plc:fromxrpc\",\"alsoKnownAs\":[\"at://someone-else.example.com\"]," +
            "\"service\":[{\"id\":\"#atproto_pds\",\"type\":\"AtprotoPersonalDataServer\",\"serviceEndpoint\":\"https://pds.test\"}]}";
        var handler = new FakeHttpHandler(r =>
            IsXrpc(r) ? FakeHttpHandler.Json("{\"did\":\"did:plc:fromxrpc\"}")
            : FakeHttpHandler.Json(didDoc));
        using var resolver = new IdentityResolver(new HttpClient(handler), new IdentityResolverOptions
        {
            PlcDirectoryUrl = "https://plc.test",
            HandleResolutionServiceUrl = Service,
            HandleResolutionOrder = new[] { HandleResolutionMethod.Xrpc },
        });

        await Assert.ThrowsAsync<IdentityResolutionException>(() => resolver.ResolveAsync(Handle));
    }

    [Fact]
    public async Task ResolveAsync_XrpcResolvedHandle_MatchingDidDocument_Succeeds()
    {
        const string didDoc = "{\"id\":\"did:plc:fromxrpc\",\"alsoKnownAs\":[\"at://alice.example.com\"]," +
            "\"service\":[{\"id\":\"#atproto_pds\",\"type\":\"AtprotoPersonalDataServer\",\"serviceEndpoint\":\"https://pds.test\"}]}";
        var handler = new FakeHttpHandler(r =>
            IsXrpc(r) ? FakeHttpHandler.Json("{\"did\":\"did:plc:fromxrpc\"}")
            : FakeHttpHandler.Json(didDoc));
        using var resolver = new IdentityResolver(new HttpClient(handler), new IdentityResolverOptions
        {
            PlcDirectoryUrl = "https://plc.test",
            HandleResolutionServiceUrl = Service,
            HandleResolutionOrder = new[] { HandleResolutionMethod.Xrpc },
        });

        var doc = await resolver.ResolveAsync(Handle);

        Assert.Equal("did:plc:fromxrpc", doc.Id);
        Assert.Contains(handler.Requests, r => r.RequestUri!.ToString() == "https://plc.test/did:plc:fromxrpc");
    }

    [Fact]
    public void Options_DuplicateMethods_AreRemoved()
    {
        using var resolver = new IdentityResolver(new HttpClient(new FakeHttpHandler(_ => FakeHttpHandler.Text(""))), new IdentityResolverOptions
        {
            DnsResolver = NoDns(),
            HandleResolutionOrder = new[] { HandleResolutionMethod.WellKnown, HandleResolutionMethod.Dns, HandleResolutionMethod.WellKnown },
        });

        Assert.Equal(new[] { HandleResolutionMethod.WellKnown, HandleResolutionMethod.Dns }, resolver.HandleResolutionOrder);
    }

    [Fact]
    public void Options_EmptyOrder_UsesDefault()
    {
        using var resolver = new IdentityResolver(new HttpClient(new FakeHttpHandler(_ => FakeHttpHandler.Text(""))), new IdentityResolverOptions
        {
            DnsResolver = NoDns(),
            HandleResolutionOrder = Array.Empty<HandleResolutionMethod>(),
        });

        Assert.Equal(IdentityResolverOptions.DefaultHandleResolutionOrder, resolver.HandleResolutionOrder);
    }

    [Fact]
    public void XrpcHandleResolver_InvalidServiceUrl_Throws()
    {
        var http = new HttpClient(new FakeHttpHandler(_ => FakeHttpHandler.Text("")));

        Assert.Throws<ArgumentException>(() => new XrpcHandleResolver(http, "not a url"));
        Assert.Throws<ArgumentException>(() => new XrpcHandleResolver(http, "ftp://example.com"));
        Assert.Throws<ArgumentException>(() => new IdentityResolver(http, new IdentityResolverOptions { HandleResolutionServiceUrl = "relative/path" }));
    }
}
