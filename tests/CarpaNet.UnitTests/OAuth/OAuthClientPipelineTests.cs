using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CarpaNet.Auth;
using CarpaNet.Blob;
using CarpaNet.Identity;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using CarpaNet.UnitTests.Http;
using Xunit;

namespace CarpaNet.UnitTests.OAuth;

/// <summary>
/// Tests for the OAuth client's request pipeline: DPoP credentials only for the session's PDS,
/// nonce retries, blob upload through DPoP, and session invalidation on a rejected refresh.
/// </summary>
public class OAuthClientPipelineTests
{
    private const string Did = "did:plc:oauthuser";
    private const string Pds = "https://pds.oauth.example";
    private const string Issuer = "https://auth.oauth.example";
    private const string TokenEndpoint = "https://auth.oauth.example/oauth/token";

    [Fact]
    public async Task Get_OwnPds_SendsDPoPProofAndToken()
    {
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Json("{\"value\":1}"));
        using var client = await CreateClientAsync(handler);

        await client.GetAsync<JsonElement>("com.example.get");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("DPoP access-1", request.Header("Authorization"));
        var proof = DecodePayload(request.Header("DPoP")!);
        Assert.Equal("GET", proof.GetProperty("htm").GetString());
        Assert.StartsWith(Pds + "/xrpc/com.example.get", proof.GetProperty("htu").GetString());
    }

    [Fact]
    public async Task ServiceUrl_SendsNoDPoPCredentials()
    {
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Json("{\"value\":1}"));
        using var client = await CreateClientAsync(handler);

        await client.WithServiceUrl(new Uri("https://video.example")).GetAsync<JsonElement>("app.bsky.video.getJobStatus");

        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Header("Authorization"));
        Assert.Null(request.Header("DPoP"));
    }

    [Fact]
    public async Task UseDPoPNonceChallenge_IsRetriedOnceWithTheNewNonce()
    {
        var calls = 0;
        var handler = new RecordingHandler(_ =>
        {
            if (++calls == 1)
            {
                var challenge = XrpcRequestPipelineTests.Json("{\"error\":\"use_dpop_nonce\"}", HttpStatusCode.Unauthorized);
                challenge.Headers.TryAddWithoutValidation("WWW-Authenticate", "DPoP error=\"use_dpop_nonce\", error_description=\"Resource server requires nonce in DPoP proof\"");
                challenge.Headers.TryAddWithoutValidation("DPoP-Nonce", "nonce-123");
                return challenge;
            }

            return XrpcRequestPipelineTests.Json("{\"value\":1}");
        });
        using var client = await CreateClientAsync(handler);

        await client.GetAsync<JsonElement>("com.example.get");

        Assert.Equal(2, handler.Requests.Count);
        Assert.False(DecodePayload(handler.Requests[0].Header("DPoP")!).TryGetProperty("nonce", out _));
        Assert.Equal("nonce-123", DecodePayload(handler.Requests[1].Header("DPoP")!).GetProperty("nonce").GetString());
        Assert.DoesNotContain(handler.Requests, r => r.Uri.ToString() == TokenEndpoint);
    }

    [Fact]
    public async Task UploadBlob_UsesDPoP()
    {
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Json(
            "{\"blob\":{\"$type\":\"blob\",\"ref\":{\"$link\":\"bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy\"},\"mimeType\":\"image/jpeg\",\"size\":3}}"));
        using var client = await CreateClientAsync(handler);

        await client.UploadBlobAsync(new MemoryStream(new byte[] { 1, 2, 3 }), "image/jpeg");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("DPoP access-1", request.Header("Authorization"));
        Assert.Equal("POST", DecodePayload(request.Header("DPoP")!).GetProperty("htm").GetString());
        Assert.Equal(new byte[] { 1, 2, 3 }, request.Body);
    }

    [Fact]
    public async Task SetLabelerDids_ChangesHeader()
    {
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Json("{\"value\":1}"));
        using var client = await CreateClientAsync(handler);

        client.SetLabelerDids(new[] { AcceptLabelersHeader.Redact("did:plc:mod") });
        await client.GetAsync<JsonElement>("com.example.get");

        Assert.Equal("did:plc:mod;redact", Assert.Single(handler.Requests).Header("atproto-accept-labelers"));
    }

    [Fact]
    public async Task RejectedRefresh_RaisesSessionInvalidated()
    {
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Json(
            "{\"error\":\"invalid_grant\",\"error_description\":\"refresh token revoked\"}", HttpStatusCode.BadRequest));
        var provider = await CreateProviderAsync(handler);
        var events = new List<SessionInvalidatedEventArgs>();
        provider.SessionInvalidated += (_, e) => events.Add(e);

        await Assert.ThrowsAsync<TokenRefreshException>(() => provider.RefreshAsync());

        var raised = Assert.Single(events);
        Assert.Equal(Did, raised.Did);
        Assert.Equal("invalid_grant", raised.Reason);
        Assert.False(provider.HasValidToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.RefreshAsync());
        Assert.Single(events);
    }

    [Fact]
    public async Task TemporaryRefreshFailure_DoesNotInvalidate()
    {
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Json(
            "{\"error\":\"server_error\"}", HttpStatusCode.InternalServerError));
        var provider = await CreateProviderAsync(handler);
        var raised = false;
        provider.SessionInvalidated += (_, _) => raised = true;

        await Assert.ThrowsAsync<TokenRefreshException>(() => provider.RefreshAsync());

        Assert.False(raised);
        Assert.NotNull(provider.RefreshToken);
    }

    [Fact]
    public async Task Refresh_IssuerStillAuthoritative_RefreshesAndKeepsAudienceWithoutSlash()
    {
        var handler = new RecordingHandler(r => IdentityAndTokenResponses(r, Issuer));
        var provider = await CreateProviderAsync(handler, withResolver: true);

        await provider.RefreshAsync();

        Assert.Equal("access-2", provider.AccessToken);
        Assert.Equal(new Uri(Pds), provider.PdsUrl);
        Assert.Contains(handler.Requests, r => r.Uri.ToString() == TokenEndpoint);
        Assert.Contains(handler.Requests, r => r.Uri.AbsolutePath == "/.well-known/oauth-protected-resource");
    }

    [Fact]
    public async Task Refresh_IssuerNoLongerAuthoritative_InvalidatesWithoutTokenRequest()
    {
        var handler = new RecordingHandler(r => IdentityAndTokenResponses(r, "https://other-auth.example"));
        var provider = await CreateProviderAsync(handler, withResolver: true);
        var events = new List<SessionInvalidatedEventArgs>();
        provider.SessionInvalidated += (_, e) => events.Add(e);

        await Assert.ThrowsAsync<TokenRefreshException>(() => provider.RefreshAsync());

        Assert.Equal("issuer_mismatch", Assert.Single(events).Reason);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.ToString() == TokenEndpoint);
        Assert.False(provider.HasValidToken);
    }

    [Fact]
    public async Task Refresh_IdentityResolutionFails_IsTemporary()
    {
        var handler = new RecordingHandler(r => r.Uri.Host == "plc.directory"
            ? XrpcRequestPipelineTests.Status(HttpStatusCode.ServiceUnavailable)
            : IdentityAndTokenResponses(r, Issuer));
        var provider = await CreateProviderAsync(handler, withResolver: true);
        var raised = false;
        provider.SessionInvalidated += (_, _) => raised = true;

        await Assert.ThrowsAsync<TokenRefreshException>(() => provider.RefreshAsync());

        Assert.False(raised);
        Assert.Equal("refresh-1", provider.RefreshToken);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.ToString() == TokenEndpoint);
    }

    [Fact]
    public async Task RateLimitedRequest_IsRetriedWithAFreshDPoPProof()
    {
        var calls = 0;
        var inner = new RecordingHandler(_ =>
        {
            if (++calls == 1)
            {
                var limited = XrpcRequestPipelineTests.Status((HttpStatusCode)429);
                limited.Headers.Add("Retry-After", "0");
                return limited;
            }

            return XrpcRequestPipelineTests.Json("{\"value\":1}");
        });
        using var rateLimit = new CarpaNet.Http.RateLimitHandler(inner) { AutoRetryOnRateLimit = true, MaxRetries = 3 };
        using var client = await CreateClientAsync(inner, new HttpClient(rateLimit));

        await client.GetAsync<JsonElement>("com.example.get");

        Assert.Equal(2, inner.Requests.Count);
        var first = DecodePayload(inner.Requests[0].Header("DPoP")!).GetProperty("jti").GetString();
        var second = DecodePayload(inner.Requests[1].Header("DPoP")!).GetProperty("jti").GetString();
        Assert.NotEqual(first, second);
        Assert.Equal("DPoP access-1", inner.Requests[1].Header("Authorization"));
    }

    [Fact]
    public async Task RateLimitHandler_DPoPRequestWithoutPreparer_IsNotRetried()
    {
        var inner = new RecordingHandler(_ =>
        {
            var limited = XrpcRequestPipelineTests.Status((HttpStatusCode)429);
            limited.Headers.Add("Retry-After", "0");
            return limited;
        });
        using var rateLimit = new CarpaNet.Http.RateLimitHandler(inner) { AutoRetryOnRateLimit = true, MaxRetries = 3 };
        using var http = new HttpClient(rateLimit);
        using var request = new HttpRequestMessage(HttpMethod.Get, Pds + "/xrpc/com.example.get");
        request.Headers.Add("DPoP", "proof");

        using var response = await http.SendAsync(request);

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task DisposingClientOrSession_DoesNotDisposeCallerIdentityResolver()
    {
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Json("{}"));
        var resolver = new IdentityResolver();   // owns its HttpClient
        var session = new OAuthSession(new OAuthClientConfig
        {
            ClientId = "https://app.example/client-metadata.json",
            RedirectUri = "https://app.example/callback",
            HttpClient = new HttpClient(handler),
            IdentityResolver = resolver,
        });
        var client = await CreateClientAsync(handler, identityResolver: resolver);

        client.Dispose();
        session.Dispose();

        var resolverHttp = (HttpClient)typeof(IdentityResolver)
            .GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(resolver)!;
        resolverHttp.Timeout = TimeSpan.FromSeconds(5);   // throws ObjectDisposedException if disposed
        resolver.Dispose();
        Assert.Throws<ObjectDisposedException>(() => resolverHttp.Timeout = TimeSpan.FromSeconds(6));
    }

    private static HttpResponseMessage IdentityAndTokenResponses(RecordedRequest request, string protectingIssuer)
    {
        if (request.Uri.Host == "plc.directory")
        {
            return XrpcRequestPipelineTests.Json(
                $"{{\"id\":\"{Did}\",\"alsoKnownAs\":[\"at://oauthuser.example\"],\"service\":[{{\"id\":\"#atproto_pds\",\"type\":\"AtprotoPersonalDataServer\",\"serviceEndpoint\":\"{Pds}/\"}}]}}");
        }

        if (request.Uri.AbsolutePath == "/.well-known/oauth-protected-resource")
        {
            return XrpcRequestPipelineTests.Json(
                $"{{\"resource\":\"{Pds}\",\"authorization_servers\":[\"{protectingIssuer}\"]}}");
        }

        if (request.Uri.ToString() == TokenEndpoint)
        {
            return XrpcRequestPipelineTests.Json(
                $"{{\"access_token\":\"access-2\",\"token_type\":\"DPoP\",\"expires_in\":3600,\"refresh_token\":\"refresh-2\",\"scope\":\"atproto\",\"sub\":\"{Did}\"}}");
        }

        return XrpcRequestPipelineTests.Status(HttpStatusCode.NotFound);
    }

    private static async Task<DPoPTokenProvider> CreateProviderAsync(RecordingHandler handler, bool withResolver = false)
    {
        var provider = new DPoPTokenProvider(
            new HttpClient(handler),
            new MemoryOAuthSessionStore(),
            clientId: "https://app.example/client-metadata.json",
            identityResolver: withResolver ? new IdentityResolver(new HttpClient(handler), cache: new MemoryIdentityCache()) : null);
        var tokenSet = new TokenSet
        {
            Issuer = Issuer,
            Sub = Did,
            Audience = Pds,
            Scope = "atproto",
            AccessToken = "access-1",
            RefreshToken = "refresh-1",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        var metadata = new OAuthAuthorizationServerMetadata
        {
            Issuer = Issuer,
            TokenEndpoint = TokenEndpoint,
            AuthorizationEndpoint = Issuer + "/oauth/authorize",
        };

        await provider.SetupAsync(Did, tokenSet, DPoPKeyPair.Generate(), metadata);
        return provider;
    }

    private static async Task<ATProtoOAuthClient> CreateClientAsync(RecordingHandler handler, HttpClient? httpClient = null, IdentityResolver? identityResolver = null)
    {
        var provider = await CreateProviderAsync(handler);
        var session = new OAuthSession(new OAuthClientConfig
        {
            ClientId = "https://app.example/client-metadata.json",
            RedirectUri = "https://app.example/callback",
            HttpClient = new HttpClient(handler),
        });

        return new ATProtoOAuthClient(
            Did,
            Pds,
            provider,
            session,
            appState: null,
            identityResolver: identityResolver ?? new IdentityResolver(new HttpClient(handler), cache: new MemoryIdentityCache()),
            jsonOptions: TestHelpers.CreateJsonOptions(),
            httpClient: httpClient ?? new HttpClient(handler));
    }

    private static JsonElement DecodePayload(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement.Clone();
    }
}
