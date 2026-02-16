using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet;
using CarpaNet.Auth;
using CarpaNet.Identity;
using Xunit;

namespace CarpaNet.UnitTests.Identity;

public class IdentityResolverTests
{
    // Test identities
    private const string TestPlcDid = "did:plc:yhgc5rlqhoezrx6fbawajxlh";
    private const string TestHandle = "drasticactions.dev";
    private const string TestWebDid = "did:web:lizthegrey.com";

    [Fact]
    public async Task ResolvePlcDid_ResolvesValidDid()
    {
        using var resolver = new IdentityResolver();

        var doc = await resolver.ResolvePlcDidAsync(TestPlcDid);

        Assert.NotNull(doc);
        Assert.Equal(TestPlcDid, doc.Id);
        Assert.NotNull(doc.Handle);
        Assert.NotNull(doc.PdsEndpoint);
    }

    [Fact]
    public async Task ResolveDidAsync_WithPlcDid_ResolvesCorrectly()
    {
        using var resolver = new IdentityResolver();

        var doc = await resolver.ResolveDidAsync(TestPlcDid);

        Assert.NotNull(doc);
        Assert.Equal(TestPlcDid, doc.Id);
    }

    [Fact]
    public async Task ResolveDidAsync_WithWebDid_ResolvesCorrectly()
    {
        using var resolver = new IdentityResolver();

        var doc = await resolver.ResolveDidAsync(TestWebDid);

        Assert.NotNull(doc);
        Assert.Equal(TestWebDid, doc.Id);
    }

    [Fact]
    public async Task ResolveDidAsync_WithInvalidDid_ThrowsException()
    {
        using var resolver = new IdentityResolver();

        await Assert.ThrowsAsync<ArgumentException>(
            () => resolver.ResolveDidAsync("invalid-did"));
    }

    [Fact]
    public async Task ResolveDidAsync_WithUnsupportedMethod_ThrowsException()
    {
        using var resolver = new IdentityResolver();

        await Assert.ThrowsAsync<IdentityResolutionException>(
            () => resolver.ResolveDidAsync("did:key:z6MkhaXgBZDvotDkL5257faiztiGiC2QtKLGpbnnEGta2doK"));
    }

    [Fact]
    public async Task ResolveHandleAsync_ViaHttps_ResolvesHandle()
    {
        using var resolver = new IdentityResolver();

        var did = await resolver.ResolveHandleAsync(TestHandle);

        Assert.NotNull(did);
        Assert.StartsWith("did:", did);
    }

    [Fact]
    public async Task ResolveAsync_WithHandle_ResolvesAndVerifies()
    {
        using var resolver = new IdentityResolver();

        var doc = await resolver.ResolveAsync(TestHandle);

        Assert.NotNull(doc);
        Assert.StartsWith("did:", doc.Id);
        Assert.NotNull(doc.PdsEndpoint);
    }

    [Fact]
    public async Task ResolveAsync_WithAtPrefix_RemovesPrefixAndResolves()
    {
        using var resolver = new IdentityResolver();

        var doc = await resolver.ResolveAsync("@" + TestHandle);

        Assert.NotNull(doc);
        Assert.StartsWith("did:", doc.Id);
    }

    [Fact]
    public void IsValidHandle_WithValidHandle_ReturnsTrue()
    {
        Assert.True(IdentityResolver.IsValidHandle("alice.bsky.social"));
        Assert.True(IdentityResolver.IsValidHandle("jay.bsky.team"));
        Assert.True(IdentityResolver.IsValidHandle("8.cn"));
        Assert.True(IdentityResolver.IsValidHandle("a.co"));
        Assert.True(IdentityResolver.IsValidHandle("xn--notarealidn.com"));
    }

    [Fact]
    public void IsValidHandle_WithInvalidHandle_ReturnsFalse()
    {
        Assert.False(IdentityResolver.IsValidHandle(""));
        Assert.False(IdentityResolver.IsValidHandle("singlepart"));
        Assert.False(IdentityResolver.IsValidHandle("jo@hn.test"));
        Assert.False(IdentityResolver.IsValidHandle("john..test"));
        Assert.False(IdentityResolver.IsValidHandle(".john.test"));
        Assert.False(IdentityResolver.IsValidHandle("john.test."));
        Assert.False(IdentityResolver.IsValidHandle("john.0")); // TLD starts with digit
        Assert.False(IdentityResolver.IsValidHandle("-john.test")); // Starts with hyphen
        Assert.False(IdentityResolver.IsValidHandle("john-.test")); // Ends with hyphen
    }

    [Fact]
    public void IsValidDid_WithValidDid_ReturnsTrue()
    {
        Assert.True(IdentityResolver.IsValidDid("did:plc:z72i7hdynmk6r22z27h6tvur"));
        Assert.True(IdentityResolver.IsValidDid("did:web:blueskyweb.xyz"));
        Assert.True(IdentityResolver.IsValidDid("did:method:val:two"));
        Assert.True(IdentityResolver.IsValidDid("did:m:v"));
    }

    [Fact]
    public void IsValidDid_WithInvalidDid_ReturnsFalse()
    {
        Assert.False(IdentityResolver.IsValidDid(""));
        Assert.False(IdentityResolver.IsValidDid("notadid"));
        Assert.False(IdentityResolver.IsValidDid("did:METHOD:val")); // Uppercase method
        Assert.False(IdentityResolver.IsValidDid("DID:method:val")); // Uppercase DID prefix
        Assert.False(IdentityResolver.IsValidDid("did:method:")); // Ends with colon
    }
}

public class DidDocumentTests
{
    private const string SampleDidDocument = @"{
        ""@context"": [
            ""https://www.w3.org/ns/did/v1"",
            ""https://w3id.org/security/multikey/v1"",
            ""https://w3id.org/security/suites/secp256k1-2019/v1""
        ],
        ""id"": ""did:plc:z72i7hdynmk6r22z27h6tvur"",
        ""alsoKnownAs"": [
            ""at://bsky.app""
        ],
        ""verificationMethod"": [
            {
                ""id"": ""did:plc:z72i7hdynmk6r22z27h6tvur#atproto"",
                ""type"": ""Multikey"",
                ""controller"": ""did:plc:z72i7hdynmk6r22z27h6tvur"",
                ""publicKeyMultibase"": ""zQ3shXjHeiBuRCKmM36cuYnm7YEMzhGnCmCyW92sRJ9pribSF""
            }
        ],
        ""service"": [
            {
                ""id"": ""#atproto_pds"",
                ""type"": ""AtprotoPersonalDataServer"",
                ""serviceEndpoint"": ""https://bsky.network""
            }
        ]
    }";

    [Fact]
    public void FromJson_ParsesDidDocument()
    {
        var doc = DidDocument.FromJson(SampleDidDocument);

        Assert.NotNull(doc);
        Assert.Equal("did:plc:z72i7hdynmk6r22z27h6tvur", doc.Id);
    }

    [Fact]
    public void Handle_ExtractsHandleFromAlsoKnownAs()
    {
        var doc = DidDocument.FromJson(SampleDidDocument);

        Assert.Equal("bsky.app", doc.Handle);
    }

    [Fact]
    public void AtprotoSigningKey_FindsSigningKey()
    {
        var doc = DidDocument.FromJson(SampleDidDocument);

        var key = doc.AtprotoSigningKey;
        Assert.NotNull(key);
        Assert.Equal("Multikey", key!.Type);
        Assert.NotNull(key.PublicKeyMultibase);
    }

    [Fact]
    public void PdsService_FindsPdsEndpoint()
    {
        var doc = DidDocument.FromJson(SampleDidDocument);

        var pds = doc.PdsService;
        Assert.NotNull(pds);
        Assert.Equal("AtprotoPersonalDataServer", pds!.Type);
        Assert.Equal("https://bsky.network", pds.ServiceEndpoint);
    }

    [Fact]
    public void PdsEndpoint_ReturnsEndpointUrl()
    {
        var doc = DidDocument.FromJson(SampleDidDocument);

        Assert.Equal("https://bsky.network", doc.PdsEndpoint);
    }

    [Fact]
    public void PublicKeyMultibase_ReturnsKey()
    {
        var doc = DidDocument.FromJson(SampleDidDocument);

        Assert.NotNull(doc.PublicKeyMultibase);
        Assert.StartsWith("z", doc.PublicKeyMultibase);
    }

    [Fact]
    public void ToJson_SerializesAndDeserializes()
    {
        var doc = DidDocument.FromJson(SampleDidDocument);
        var json = doc.ToJson();
        var doc2 = DidDocument.FromJson(json);

        Assert.Equal(doc.Id, doc2.Id);
        Assert.Equal(doc.Handle, doc2.Handle);
        Assert.Equal(doc.PdsEndpoint, doc2.PdsEndpoint);
    }

    [Fact]
    public void VerificationMethod_IsMultikey_ReturnsTrue()
    {
        var vm = new VerificationMethod
        {
            Type = "Multikey",
            PublicKeyMultibase = "zQ3sh..."
        };

        Assert.True(vm.IsMultikey);
        Assert.False(vm.IsLegacy);
    }

    [Fact]
    public void VerificationMethod_IsLegacy_ReturnsTrue()
    {
        var vmP256 = new VerificationMethod
        {
            Type = "EcdsaSecp256r1VerificationKey2019"
        };

        var vmK256 = new VerificationMethod
        {
            Type = "EcdsaSecp256k1VerificationKey2019"
        };

        Assert.True(vmP256.IsLegacy);
        Assert.Equal("P-256", vmP256.LegacyCurve);

        Assert.True(vmK256.IsLegacy);
        Assert.Equal("secp256k1", vmK256.LegacyCurve);
    }
}

public class IdentityResolutionExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        var ex = new IdentityResolutionException("Test message");

        Assert.Equal("Test message", ex.Message);
    }

    [Fact]
    public void Constructor_WithInnerException_SetsInnerException()
    {
        var inner = new Exception("Inner");
        var ex = new IdentityResolutionException("Outer", inner);

        Assert.Equal("Outer", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }
}

public class ATUriAuthorityTests
{
    [Fact]
    public void AuthorityIsDid_WithDidAuthority_ReturnsTrue()
    {
        var uri = new ATUri("at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/123");

        Assert.True(uri.AuthorityIsDid);
        Assert.False(uri.AuthorityIsHandle);
    }

    [Fact]
    public void AuthorityIsHandle_WithHandleAuthority_ReturnsTrue()
    {
        var uri = new ATUri("at://alice.bsky.social/app.bsky.feed.post/123");

        Assert.False(uri.AuthorityIsDid);
        Assert.True(uri.AuthorityIsHandle);
    }

    [Fact]
    public void AuthorityAsDid_WithDidAuthority_ReturnsDid()
    {
        var uri = new ATUri("at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/123");

        var did = uri.AuthorityAsDid;
        Assert.NotNull(did);
        Assert.Equal("did:plc:z72i7hdynmk6r22z27h6tvur", did!.Value.Value);
    }

    [Fact]
    public void AuthorityAsDid_WithHandleAuthority_ReturnsNull()
    {
        var uri = new ATUri("at://alice.bsky.social/app.bsky.feed.post/123");

        Assert.Null(uri.AuthorityAsDid);
    }

    [Fact]
    public void AuthorityAsHandle_WithHandleAuthority_ReturnsHandle()
    {
        var uri = new ATUri("at://alice.bsky.social/app.bsky.feed.post/123");

        var handle = uri.AuthorityAsHandle;
        Assert.NotNull(handle);
        Assert.Equal("alice.bsky.social", handle!.Value.Value);
    }

    [Fact]
    public void AuthorityAsHandle_WithDidAuthority_ReturnsNull()
    {
        var uri = new ATUri("at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/123");

        Assert.Null(uri.AuthorityAsHandle);
    }

    [Fact]
    public async Task ResolvePdsEndpointAsync_WithDidAuthority_ReturnsEndpoint()
    {
        using var resolver = new IdentityResolver();
        var uri = new ATUri("at://did:plc:yhgc5rlqhoezrx6fbawajxlh/app.bsky.feed.post/123");

        var endpoint = await uri.ResolvePdsEndpointAsync(resolver);

        Assert.NotNull(endpoint);
        Assert.StartsWith("https://", endpoint);
    }

    [Fact]
    public async Task ResolvePdsEndpointAsync_WithHandleAuthority_ReturnsEndpoint()
    {
        using var resolver = new IdentityResolver();
        var uri = new ATUri("at://drasticactions.dev/app.bsky.feed.post/123");

        var endpoint = await uri.ResolvePdsEndpointAsync(resolver);

        Assert.NotNull(endpoint);
        Assert.StartsWith("https://", endpoint);
    }

    [Fact]
    public async Task ResolvePdsEndpointAsync_WithNullResolver_ThrowsArgumentNullException()
    {
        var uri = new ATUri("at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/123");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => uri.ResolvePdsEndpointAsync(null!));
    }

    [Fact]
    public async Task ResolvePdsEndpointAsync_WithEmptyAuthority_ThrowsArgumentException()
    {
        using var resolver = new IdentityResolver();
        var uri = new ATUri("invalid-uri");

        await Assert.ThrowsAsync<ArgumentException>(
            () => uri.ResolvePdsEndpointAsync(resolver));
    }
}

public class ATProtoClientExtensionsTests
{
    // Mock client for testing extensions
    private class MockATProtoClient : IATProtoClient
    {
        public Uri BaseUrl { get; set; } = new Uri("https://bsky.social");
        public bool IsAuthenticated { get; set; }
        public string? AuthenticatedDid { get; set; }
        public IdentityResolver? IdentityResolver { get; set; }
        public IReadOnlyList<string>? LabelerDids { get; set; }

        public ITokenProvider? TokenProvider => throw new NotImplementedException();

        public HttpClient HttpClient => throw new NotImplementedException();

        public Task<TOutput> GetAsync<TOutput>(string nsid, IReadOnlyDictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<TOutput> GetAsync<TOutput>(string nsid, string proxyServiceDid, IReadOnlyDictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<TOutput> PostAsync<TInput, TOutput>(string nsid, TInput? input, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<TOutput> PostAsync<TInput, TOutput>(string nsid, string proxyServiceDid, TInput? input, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public IAsyncEnumerable<TMessage> SubscribeAsync<TMessage>(string nsid, IReadOnlyDictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task ResolveAuthorityAsync_WithNoResolver_ThrowsInvalidOperationException()
    {
        var client = new MockATProtoClient { IdentityResolver = null };
        var uri = new ATUri("at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/123");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ResolveAuthorityAsync(uri));
    }

    [Fact]
    public async Task ResolveAuthorityAsync_WithValidDidUri_ReturnsPdsEndpoint()
    {
        var client = new MockATProtoClient { IdentityResolver = new IdentityResolver() };
        var uri = new ATUri("at://did:plc:yhgc5rlqhoezrx6fbawajxlh/app.bsky.feed.post/123");

        var endpoint = await client.ResolveAuthorityAsync(uri);

        Assert.NotNull(endpoint);
        Assert.StartsWith("https://", endpoint);
    }

    [Fact]
    public async Task ResolveAuthorityAsync_WithValidHandleUri_ReturnsPdsEndpoint()
    {
        var client = new MockATProtoClient { IdentityResolver = new IdentityResolver() };
        var uri = new ATUri("at://drasticactions.dev/app.bsky.feed.post/123");

        var endpoint = await client.ResolveAuthorityAsync(uri);

        Assert.NotNull(endpoint);
        Assert.StartsWith("https://", endpoint);
    }

    [Fact]
    public async Task ResolveAuthorityToDidDocumentAsync_WithValidUri_ReturnsDidDocument()
    {
        var client = new MockATProtoClient { IdentityResolver = new IdentityResolver() };
        var uri = new ATUri("at://did:plc:yhgc5rlqhoezrx6fbawajxlh/app.bsky.feed.post/123");

        var doc = await client.ResolveAuthorityToDidDocumentAsync(uri);

        Assert.NotNull(doc);
        Assert.Equal("did:plc:yhgc5rlqhoezrx6fbawajxlh", doc.Id);
    }

    [Fact]
    public async Task ResolveAuthorityAsync_WithEmptyAuthority_ThrowsArgumentException()
    {
        var client = new MockATProtoClient { IdentityResolver = new IdentityResolver() };
        var uri = new ATUri("invalid-uri");

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ResolveAuthorityAsync(uri));
    }
}

public class NsidRoutingTests
{
    [Fact]
    public void GetServiceCategory_AtProtoNamespace_ReturnsAtProto()
    {
        Assert.Equal(NsidServiceCategory.AtProto, ATProtoClientExtensions.GetServiceCategory("com.atproto.repo.createRecord"));
        Assert.Equal(NsidServiceCategory.AtProto, ATProtoClientExtensions.GetServiceCategory("com.atproto.server.createSession"));
        Assert.Equal(NsidServiceCategory.AtProto, ATProtoClientExtensions.GetServiceCategory("com.atproto.sync.subscribeRepos"));
    }

    [Fact]
    public void GetServiceCategory_BlueskyAppNamespace_ReturnsBlueskyApp()
    {
        Assert.Equal(NsidServiceCategory.BlueskyApp, ATProtoClientExtensions.GetServiceCategory("app.bsky.feed.getTimeline"));
        Assert.Equal(NsidServiceCategory.BlueskyApp, ATProtoClientExtensions.GetServiceCategory("app.bsky.actor.getProfile"));
        Assert.Equal(NsidServiceCategory.BlueskyApp, ATProtoClientExtensions.GetServiceCategory("app.bsky.notification.listNotifications"));
    }

    [Fact]
    public void GetServiceCategory_ChatNamespace_ReturnsBlueskyChat()
    {
        Assert.Equal(NsidServiceCategory.BlueskyChat, ATProtoClientExtensions.GetServiceCategory("chat.bsky.convo.listConvos"));
        Assert.Equal(NsidServiceCategory.BlueskyChat, ATProtoClientExtensions.GetServiceCategory("chat.bsky.convo.sendMessage"));
    }

    [Fact]
    public void GetServiceCategory_OzoneNamespace_ReturnsOzone()
    {
        Assert.Equal(NsidServiceCategory.Ozone, ATProtoClientExtensions.GetServiceCategory("tools.ozone.moderation.getRepo"));
        Assert.Equal(NsidServiceCategory.Ozone, ATProtoClientExtensions.GetServiceCategory("tools.ozone.moderation.emitEvent"));
    }

    [Fact]
    public void GetServiceCategory_UnknownNamespace_ReturnsUnknown()
    {
        Assert.Equal(NsidServiceCategory.Unknown, ATProtoClientExtensions.GetServiceCategory("custom.app.something"));
        Assert.Equal(NsidServiceCategory.Unknown, ATProtoClientExtensions.GetServiceCategory(""));
        Assert.Equal(NsidServiceCategory.Unknown, ATProtoClientExtensions.GetServiceCategory(null!));
    }

    [Fact]
    public void GetProxyServiceDid_ChatNamespace_ReturnsChatServiceDid()
    {
        var serviceDid = ATProtoClientExtensions.GetProxyServiceDid("chat.bsky.convo.listConvos");

        Assert.Equal(BlueskyServices.ChatServiceDid, serviceDid);
    }

    [Fact]
    public void GetProxyServiceDid_OzoneNamespace_ReturnsOzoneServiceDid()
    {
        var serviceDid = ATProtoClientExtensions.GetProxyServiceDid("tools.ozone.moderation.getRepo");

        Assert.Equal(BlueskyServices.OzoneServiceDid, serviceDid);
    }

    [Fact]
    public void GetProxyServiceDid_AtProtoNamespace_ReturnsNull()
    {
        var serviceDid = ATProtoClientExtensions.GetProxyServiceDid("com.atproto.repo.createRecord");

        Assert.Null(serviceDid);
    }

    [Fact]
    public void GetProxyServiceDid_BlueskyAppNamespace_ReturnsNull()
    {
        // PDS handles app.bsky.* proxying automatically
        var serviceDid = ATProtoClientExtensions.GetProxyServiceDid("app.bsky.feed.getTimeline");

        Assert.Null(serviceDid);
    }

    [Fact]
    public void IsWriteOperation_RepoCreateRecord_ReturnsTrue()
    {
        Assert.True(ATProtoClientExtensions.IsWriteOperation("com.atproto.repo.createRecord"));
        Assert.True(ATProtoClientExtensions.IsWriteOperation("com.atproto.repo.putRecord"));
        Assert.True(ATProtoClientExtensions.IsWriteOperation("com.atproto.repo.deleteRecord"));
        Assert.True(ATProtoClientExtensions.IsWriteOperation("com.atproto.repo.applyWrites"));
    }

    [Fact]
    public void IsWriteOperation_ReadOperations_ReturnsFalse()
    {
        Assert.False(ATProtoClientExtensions.IsWriteOperation("com.atproto.repo.getRecord"));
        Assert.False(ATProtoClientExtensions.IsWriteOperation("app.bsky.feed.getTimeline"));
        Assert.False(ATProtoClientExtensions.IsWriteOperation(""));
        Assert.False(ATProtoClientExtensions.IsWriteOperation(null!));
    }
}

public class BlueskyServicesTests
{
    [Fact]
    public void ServiceConstants_HaveExpectedValues()
    {
        Assert.Equal("https://public.api.bsky.app", BlueskyServices.PublicAppView);
        Assert.Equal("https://api.bsky.app", BlueskyServices.AppView);
        Assert.Equal("https://bsky.social", BlueskyServices.Entryway);
        Assert.Equal("https://bsky.network", BlueskyServices.Relay);
        Assert.Equal("https://api.bsky.chat", BlueskyServices.Chat);
        Assert.Equal("https://mod.bsky.app", BlueskyServices.Ozone);
    }

    [Fact]
    public void ServiceDids_HaveExpectedValues()
    {
        Assert.Equal("did:web:api.bsky.app#bsky_appview", BlueskyServices.AppViewServiceDid);
        Assert.Equal("did:web:api.bsky.chat#bsky_chat", BlueskyServices.ChatServiceDid);
        Assert.Equal("did:plc:ar7c4by46qjdydhdevvrndac#atproto_labeler", BlueskyServices.OzoneServiceDid);
    }
}
