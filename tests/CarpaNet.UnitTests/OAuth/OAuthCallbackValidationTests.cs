using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Identity;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Storage;
using Xunit;

namespace CarpaNet.UnitTests.OAuth;

/// <summary>
/// Tests for the validation done by <see cref="OAuthSession.CallbackAsync"/>: the RFC 9207
/// <c>iss</c> parameter, and verification of the token response's <c>sub</c>.
/// No network access: every HTTP call is answered by <see cref="FakeOAuthServer"/>.
/// </summary>
public class OAuthCallbackValidationTests
{
    private const string Issuer = "https://auth.example.com";
    private const string OtherIssuer = "https://evil.example.com";
    private const string PdsUrl = "https://pds.example.com";
    private const string EntrywayUrl = "https://entryway.example.com";
    private const string PlcDirectory = "https://plc.test";
    private const string UserDid = "did:plc:abcdefghijklmnopqrstuvwx";
    private const string OtherDid = "did:plc:zyxwvutsrqponmlkjihgfedc";
    private const string RedirectUri = "http://127.0.0.1:8080/callback";

    [Fact]
    public async Task Callback_IssMismatch_ThrowsAndDoesNotRequestToken()
    {
        var server = new FakeOAuthServer();
        using var session = server.CreateSession();

        var state = await StartAsync(session, UserDid);
        var ex = await Assert.ThrowsAsync<OAuthCallbackException>(
            () => session.CallbackAsync(CallbackUrl(state, OtherIssuer)));

        Assert.Equal("issuer_mismatch", ex.ErrorCode);
        Assert.Equal(0, server.TokenRequests);
    }

    [Fact]
    public async Task Callback_IssMismatch_ThrowsEvenWhenServerDoesNotAdvertiseIss()
    {
        var server = new FakeOAuthServer { IssParameterSupported = false };
        using var session = server.CreateSession();

        var state = await StartAsync(session, UserDid);
        var ex = await Assert.ThrowsAsync<OAuthCallbackException>(
            () => session.CallbackAsync(CallbackUrl(state, OtherIssuer)));

        Assert.Equal("issuer_mismatch", ex.ErrorCode);
        Assert.Equal(0, server.TokenRequests);
    }

    [Fact]
    public async Task Callback_IssMissing_WhenRequired_ThrowsAndDoesNotRequestToken()
    {
        var server = new FakeOAuthServer { IssParameterSupported = true };
        using var session = server.CreateSession();

        var state = await StartAsync(session, UserDid, appState: "my-app-state");
        var ex = await Assert.ThrowsAsync<OAuthCallbackException>(
            () => session.CallbackAsync(CallbackUrl(state, iss: null)));

        Assert.Equal("missing_iss", ex.ErrorCode);
        Assert.Equal("my-app-state", ex.AppState);
        Assert.Equal(0, server.TokenRequests);
    }

    [Fact]
    public async Task Callback_IssMissing_WhenNotRequired_Succeeds()
    {
        var server = new FakeOAuthServer { IssParameterSupported = false };
        using var session = server.CreateSession();

        var state = await StartAsync(session, UserDid);
        using var client = await session.CallbackAsync(CallbackUrl(state, iss: null));

        Assert.Equal(UserDid, client.Did);
        Assert.Equal(1, server.TokenRequests);
    }

    [Fact]
    public async Task Callback_IssMatches_Succeeds()
    {
        var server = new FakeOAuthServer { IssParameterSupported = true };
        using var session = server.CreateSession();

        var state = await StartAsync(session, UserDid, appState: "app");
        using var client = await session.CallbackAsync(CallbackUrl(state, Issuer));

        Assert.Equal(UserDid, client.Did);
        Assert.Equal("app", client.AppState);
        Assert.Equal(new Uri(PdsUrl), client.TokenProvider.PdsUrl);
        Assert.NotNull(await server.SessionStore.GetAsync(UserDid));
        Assert.Equal(0, server.RevocationRequests);
    }

    [Fact]
    public async Task Callback_SubNotADid_RevokesAndDoesNotStoreSession()
    {
        var server = new FakeOAuthServer { TokenSub = "alice.example.com" };
        using var session = server.CreateSession();

        var state = await StartAsync(session, PdsUrl);
        var ex = await Assert.ThrowsAsync<OAuthCallbackException>(
            () => session.CallbackAsync(CallbackUrl(state, Issuer)));

        Assert.Equal("invalid_sub", ex.ErrorCode);
        Assert.Equal(1, server.RevocationRequests);
        Assert.Contains("token=refresh-token", server.LastRevocationBody);
        Assert.Null(await server.SessionStore.GetAsync("alice.example.com"));
    }

    [Fact]
    public async Task Callback_SubPdsNotProtectedByIssuer_RevokesAndDoesNotStoreSession()
    {
        var server = new FakeOAuthServer();
        server.PdsAuthorizationServers = new[] { OtherIssuer };
        using var session = server.CreateSession();

        // Start from the entryway, which (correctly) points at the issuer
        var state = await StartAsync(session, EntrywayUrl);
        var ex = await Assert.ThrowsAsync<OAuthCallbackException>(
            () => session.CallbackAsync(CallbackUrl(state, Issuer)));

        Assert.Equal("sub_issuer_mismatch", ex.ErrorCode);
        Assert.Equal(1, server.TokenRequests);
        Assert.Equal(1, server.RevocationRequests);
        Assert.Null(await server.SessionStore.GetAsync(UserDid));
    }

    [Fact]
    public async Task Callback_SubDidResolutionFails_RevokesAndThrows()
    {
        var server = new FakeOAuthServer { TokenSub = OtherDid }; // No DID document for OtherDid
        using var session = server.CreateSession();

        var state = await StartAsync(session, EntrywayUrl);
        var ex = await Assert.ThrowsAsync<OAuthCallbackException>(
            () => session.CallbackAsync(CallbackUrl(state, Issuer)));

        Assert.Equal("sub_verification_failed", ex.ErrorCode);
        Assert.Equal(1, server.RevocationRequests);
        Assert.Null(await server.SessionStore.GetAsync(OtherDid));
    }

    [Fact]
    public async Task Callback_SubDiffersFromExpectedDid_RevokesAndThrows()
    {
        var server = new FakeOAuthServer { TokenSub = OtherDid };
        server.AddDidDocument(OtherDid, PdsUrl);
        using var session = server.CreateSession();

        var state = await StartAsync(session, UserDid);
        var ex = await Assert.ThrowsAsync<OAuthCallbackException>(
            () => session.CallbackAsync(CallbackUrl(state, Issuer)));

        Assert.Equal("sub_mismatch", ex.ErrorCode);
        Assert.Equal(1, server.RevocationRequests);
        Assert.Null(await server.SessionStore.GetAsync(OtherDid));
    }

    [Fact]
    public async Task Callback_StartedFromEntryway_UsesResolvedPdsAsAudience()
    {
        var server = new FakeOAuthServer();
        using var session = server.CreateSession();

        var state = await StartAsync(session, EntrywayUrl);
        using var client = await session.CallbackAsync(CallbackUrl(state, Issuer));

        Assert.Equal(UserDid, client.Did);
        Assert.Equal(new Uri(PdsUrl), client.BaseUrl);
        Assert.Equal(new Uri(PdsUrl), client.TokenProvider.PdsUrl);

        var stored = await server.SessionStore.GetAsync(UserDid);
        Assert.NotNull(stored);
        Assert.Equal(PdsUrl, stored!.TokenSet.Audience);
        Assert.Equal(Issuer, stored.TokenSet.Issuer);
    }

    [Fact]
    public async Task Callback_StartedFromEntryway_AnyAccountMaySignIn()
    {
        var server = new FakeOAuthServer { TokenSub = OtherDid };
        server.AddDidDocument(OtherDid, "https://other-pds.example.com/");
        server.AddProtectedResource("https://other-pds.example.com", Issuer);
        using var session = server.CreateSession();

        var state = await StartAsync(session, EntrywayUrl);
        using var client = await session.CallbackAsync(CallbackUrl(state, Issuer));

        Assert.Equal(OtherDid, client.Did);
        Assert.Equal(new Uri("https://other-pds.example.com"), client.TokenProvider.PdsUrl);
    }

    [Fact]
    public async Task RestoreSession_AfterEntrywayCallback_KeepsResolvedPds()
    {
        var server = new FakeOAuthServer();

        using (var session = server.CreateSession())
        {
            var state = await StartAsync(session, EntrywayUrl);
            using var client = await session.CallbackAsync(CallbackUrl(state, Issuer));
        }

        using var restoredSession = server.CreateSession();
        using var restored = await restoredSession.RestoreSessionAsync(UserDid);

        Assert.NotNull(restored);
        Assert.Equal(UserDid, restored!.Did);
        Assert.Equal(new Uri(PdsUrl), restored.TokenProvider.PdsUrl);
        Assert.True(restored.IsAuthenticated);
    }

    private static async Task<string> StartAsync(OAuthSession session, string input, string? appState = null)
    {
        var url = await session.AuthorizeAsync(input, appState);
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        return query["state"] ?? throw new InvalidOperationException("No state in authorization URL.");
    }

    private static string CallbackUrl(string state, string? iss)
    {
        var url = $"{RedirectUri}?code=auth-code&state={Uri.EscapeDataString(state)}";
        if (iss != null)
        {
            url += $"&iss={Uri.EscapeDataString(iss)}";
        }

        return url;
    }

    /// <summary>
    /// In-memory authorization server, PDS, entryway and PLC directory.
    /// </summary>
    private sealed class FakeOAuthServer : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _didDocuments = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> _protectedResources = new(StringComparer.OrdinalIgnoreCase);

        public FakeOAuthServer()
        {
            AddDidDocument(UserDid, PdsUrl);
            AddProtectedResource(EntrywayUrl, Issuer);
        }

        public bool IssParameterSupported { get; set; } = true;

        public string TokenSub { get; set; } = UserDid;

        public string[] PdsAuthorizationServers
        {
            set => _protectedResources[PdsUrl] = value;
        }

        public MemoryOAuthSessionStore SessionStore { get; } = new();

        public MemoryOAuthStateStore StateStore { get; } = new();

        public int TokenRequests { get; private set; }

        public int RevocationRequests { get; private set; }

        public string LastRevocationBody { get; private set; } = string.Empty;

        public void AddDidDocument(string did, string pdsEndpoint)
        {
            _didDocuments[did] = $$"""
                {
                  "id": "{{did}}",
                  "alsoKnownAs": ["at://user.example.com"],
                  "service": [
                    { "id": "#atproto_pds", "type": "AtprotoPersonalDataServer", "serviceEndpoint": "{{pdsEndpoint}}" }
                  ]
                }
                """;
            _protectedResources.TryAdd(pdsEndpoint.TrimEnd('/'), new[] { Issuer });
        }

        public void AddProtectedResource(string resourceUrl, params string[] authorizationServers)
        {
            _protectedResources[resourceUrl.TrimEnd('/')] = authorizationServers;
        }

        public OAuthSession CreateSession()
        {
            var httpClient = new HttpClient(this, disposeHandler: false);
            return new OAuthSession(new OAuthClientConfig
            {
                ClientId = OAuthClientConfig.CreateLoopbackClientId(8080),
                RedirectUri = RedirectUri,
                HttpClient = httpClient,
                StateStore = StateStore,
                SessionStore = SessionStore,
                IdentityResolver = new IdentityResolver(httpClient, PlcDirectory),
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var origin = $"{uri.Scheme}://{uri.Authority}";
            var path = uri.AbsolutePath;

            if (request.Method == HttpMethod.Get && path == "/.well-known/oauth-protected-resource" &&
                _protectedResources.TryGetValue(origin, out var servers))
            {
                var list = string.Join(",", servers.Select(s => $"\"{s}\""));
                return Json($$"""{ "resource": "{{origin}}", "authorization_servers": [{{list}}] }""");
            }

            if (request.Method == HttpMethod.Get && origin == Issuer && path == "/.well-known/oauth-authorization-server")
            {
                return Json($$"""
                    {
                      "issuer": "{{Issuer}}",
                      "authorization_endpoint": "{{Issuer}}/oauth/authorize",
                      "token_endpoint": "{{Issuer}}/oauth/token",
                      "revocation_endpoint": "{{Issuer}}/oauth/revoke",
                      "authorization_response_iss_parameter_supported": {{(IssParameterSupported ? "true" : "false")}},
                      "dpop_signing_alg_values_supported": ["ES256"]
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && origin == PlcDirectory)
            {
                var did = Uri.UnescapeDataString(path.TrimStart('/'));
                return _didDocuments.TryGetValue(did, out var doc)
                    ? Json(doc)
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && uri.ToString() == $"{Issuer}/oauth/token")
            {
                TokenRequests++;
                Assert.True(request.Headers.Contains("DPoP"));
                return Json($$"""
                    {
                      "access_token": "access-token",
                      "token_type": "DPoP",
                      "expires_in": 3600,
                      "refresh_token": "refresh-token",
                      "scope": "atproto",
                      "sub": "{{TokenSub}}"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post && uri.ToString() == $"{Issuer}/oauth/revoke")
            {
                RevocationRequests++;
                LastRevocationBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
