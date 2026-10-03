using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Scopes;
using Xunit;

namespace CarpaNet.UnitTests.OAuth;

/// <summary>
/// The atproto client metadata rules from atproto/packages/oauth/{oauth-types,oauth-client,oauth-provider}
/// @ a7c8604, ported with their cases from SocialApp's OAuthClientTests.
/// </summary>
public class ClientMetadataTests
{
    private const string ExpectedScope =
        "atproto " +
        "include:app.bsky.authFullApp?aud=did:web:api.bsky.app%23bsky_appview " +
        "include:chat.bsky.authFullChatClient?aud=did:web:api.bsky.chat%23bsky_chat " +
        "blob:*/* " +
        "rpc:app.bsky.video.getUploadLimits?aud=* " +
        "rpc:com.atproto.repo.uploadBlob?aud=* " +
        "account:email " +
        "identity:handle";

    private static ScopeSet BuildScope() => new ScopeSet()
        .AddAtproto()
        .AddInclude("app.bsky.authFullApp", "did:web:api.bsky.app#bsky_appview")
        .AddInclude("chat.bsky.authFullChatClient", "did:web:api.bsky.chat#bsky_chat")
        .AddBlob("*/*")
        .AddRpc("app.bsky.video.getUploadLimits", "*")
        .AddRpc("com.atproto.repo.uploadBlob", "*")
        .AddAccount(AccountAttribute.Email)
        .AddIdentity(IdentityAttribute.Handle);

    private static OAuthClientMetadata Hosted(
        string clientId = "https://app.example.com/oauth-client-metadata.json",
        string[]? redirects = null,
        string? scope = null,
        string? clientUri = "https://app.example.com",
        string applicationType = OAuthApplicationType.Native)
    {
        var metadata = OAuthClientMetadata.CreatePublicClient(
            clientId,
            redirects ?? new[] { "com.example.app:/callback" },
            scope ?? ExpectedScope,
            applicationType);
        metadata.ClientName = "SocialApp";
        metadata.ClientUri = clientUri;
        metadata.LogoUri = "https://app.example.com/logo.png";
        metadata.TosUri = "https://app.example.com/tos";
        metadata.PolicyUri = "https://app.example.com/privacy";
        return metadata;
    }

    private static OAuthClientMetadata With(Action<OAuthClientMetadata> change)
    {
        var metadata = Hosted();
        change(metadata);
        return metadata;
    }

    private static OAuthClientMetadata MetroSocialNative(string clientUri = "https://drasticactions.vip/")
    {
        var metadata = OAuthClientMetadata.CreatePublicClient(
            "https://drasticactions.vip/client-metadata.json",
            new[] { "vip.drasticactions:/callback", "http://127.0.0.1/callback" },
            BuildScope());
        metadata.ClientName = "MetroSocial";
        metadata.ClientUri = clientUri;
        return metadata;
    }

    private static OAuthClientMetadata MetroSocialWeb()
    {
        var metadata = OAuthClientMetadata.CreatePublicClient(
            "https://drasticactions.vip/metrosocial/client-metadata.json",
            new[] { "https://drasticactions.vip/metrosocial/" },
            BuildScope(),
            OAuthApplicationType.Web);
        metadata.ClientName = "MetroSocial";
        metadata.ClientUri = "https://drasticactions.vip/metrosocial/";
        return metadata;
    }

    private static Dictionary<string, object> Fields(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var fields = new Dictionary<string, object>();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            fields[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.Array => string.Join("|", property.Value.EnumerateArray().Select(e => e.GetString())),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => property.Value.GetString()!,
            };
        }

        return fields;
    }

    // --- ScopeSet ---

    [Fact]
    public void ScopeSetBuilder_ProducesTheExactScopeString()
    {
        Assert.Equal(ExpectedScope, BuildScope().ToString());
        Assert.Null(OAuthClientMetadataValidator.CheckScope(ExpectedScope));
    }

    // --- Golden documents ---

    [Fact]
    public void Golden_NativeDocument()
    {
        var metadata = MetroSocialNative();
        metadata.Validate();

        var fields = Fields(metadata.ToJson());
        Assert.Equal(
            new Dictionary<string, object>
            {
                ["client_id"] = "https://drasticactions.vip/client-metadata.json",
                ["client_name"] = "MetroSocial",
                ["client_uri"] = "https://drasticactions.vip/",
                ["application_type"] = "native",
                ["redirect_uris"] = "vip.drasticactions:/callback|http://127.0.0.1/callback",
                ["grant_types"] = "authorization_code|refresh_token",
                ["response_types"] = "code",
                ["token_endpoint_auth_method"] = "none",
                ["dpop_bound_access_tokens"] = true,
                ["scope"] = ExpectedScope,
            }.OrderBy(p => p.Key),
            fields.OrderBy(p => p.Key));
    }

    [Fact]
    public void Golden_WebDocument()
    {
        var metadata = MetroSocialWeb();
        metadata.Validate();

        var fields = Fields(metadata.ToJson(indented: false));
        Assert.Equal(
            new Dictionary<string, object>
            {
                ["client_id"] = "https://drasticactions.vip/metrosocial/client-metadata.json",
                ["client_name"] = "MetroSocial",
                ["client_uri"] = "https://drasticactions.vip/metrosocial/",
                ["application_type"] = "web",
                ["redirect_uris"] = "https://drasticactions.vip/metrosocial/",
                ["grant_types"] = "authorization_code|refresh_token",
                ["response_types"] = "code",
                ["token_endpoint_auth_method"] = "none",
                ["dpop_bound_access_tokens"] = true,
                ["scope"] = ExpectedScope,
            }.OrderBy(p => p.Key),
            fields.OrderBy(p => p.Key));
    }

    [Fact]
    public void Golden_NativeDocumentWithTheWebClientUri_IsRejected()
    {
        var metadata = MetroSocialNative("https://drasticactions.vip/metrosocial/");
        var ex = Assert.Throws<InvalidOAuthClientMetadataException>(metadata.Validate);
        Assert.Equal("client_uri must be a parent URL of the client_id", ex.Message);
        Assert.False(metadata.TryValidate(out var error));
        Assert.Equal("client_uri must be a parent URL of the client_id", error);
    }

    // --- CreatePublicClient / CreateForLoopbackClientId ---

    [Fact]
    public void CreatePublicClient_SetsThePublicClientFields()
    {
        var metadata = OAuthClientMetadata.CreatePublicClient(
            "https://app.example.com/client-metadata.json",
            new List<string> { "com.example.app:/callback" },
            new ScopeSet().AddAtproto().AddTransitionGeneric(),
            OAuthApplicationType.Web);

        Assert.Equal("https://app.example.com/client-metadata.json", metadata.ClientId);
        Assert.Equal(new[] { "com.example.app:/callback" }, metadata.RedirectUris);
        Assert.Equal("atproto transition:generic", metadata.Scope);
        Assert.Equal("web", metadata.ApplicationType);
        Assert.Equal(new[] { "authorization_code", "refresh_token" }, metadata.GrantTypes);
        Assert.Equal(new[] { "code" }, metadata.ResponseTypes);
        Assert.Equal("none", metadata.TokenEndpointAuthMethod);
        Assert.True(metadata.DpopBoundAccessTokens);
        Assert.Null(metadata.TokenEndpointAuthSigningAlg);
        Assert.Null(metadata.Jwks);
        Assert.Null(metadata.JwksUri);
        Assert.Null(metadata.ClientName);
        Assert.Equal("native", OAuthClientMetadata.CreatePublicClient("https://a.example.com/c.json", new[] { "https://a.example.com/cb" }, "atproto").ApplicationType);
    }

    [Fact]
    public void CreateForLoopbackClientId_DerivesTheServerSideDocument()
    {
        var clientId = AtprotoLoopbackClientId.Build(ExpectedScope, new[] { "http://127.0.0.1:8080/callback" });
        var metadata = OAuthClientMetadata.CreateForLoopbackClientId(clientId);

        Assert.Equal(clientId, metadata.ClientId);
        Assert.Equal(ExpectedScope, metadata.Scope);
        Assert.Equal(new[] { "http://127.0.0.1:8080/callback" }, metadata.RedirectUris);
        Assert.Equal(new[] { "code" }, metadata.ResponseTypes);
        Assert.Equal(new[] { "authorization_code", "refresh_token" }, metadata.GrantTypes);
        Assert.Equal("none", metadata.TokenEndpointAuthMethod);
        Assert.Equal("native", metadata.ApplicationType);
        Assert.True(metadata.DpopBoundAccessTokens);
        Assert.Null(metadata.ClientName);
        Assert.Null(metadata.ClientUri);
        metadata.Validate();
    }

    [Fact]
    public void CreateForLoopbackClientId_UsesTheDefaults()
    {
        var metadata = OAuthClientMetadata.CreateForLoopbackClientId("http://localhost");

        Assert.Equal("atproto", metadata.Scope);
        Assert.Equal(new[] { "http://127.0.0.1/", "http://[::1]/" }, metadata.RedirectUris);
        Assert.True(metadata.TryValidate(out var error), error);
    }

    [Fact]
    public void CreateForLoopbackClientId_RejectsAnInvalidId()
    {
        var ex = Assert.Throws<ArgumentException>(() => OAuthClientMetadata.CreateForLoopbackClientId("http://localhost/path"));
        Assert.StartsWith("Invalid loopback client ID: Value must not contain a path component", ex.Message, StringComparison.Ordinal);
    }

    // --- JSON ---

    [Fact]
    public void Json_UsesTheExactPropertyNames_AndOmitsNulls()
    {
        var metadata = Hosted();
        metadata.TokenEndpointAuthSigningAlg = null;
        var json = metadata.ToJson();
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(
            new[]
            {
                "client_id", "redirect_uris", "response_types", "grant_types", "scope", "token_endpoint_auth_method",
                "application_type", "client_name", "client_uri", "logo_uri", "tos_uri", "policy_uri", "dpop_bound_access_tokens",
            },
            doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Contains("\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", metadata.ToJson(indented: false), StringComparison.Ordinal);
    }

    [Fact]
    public void Json_AlwaysWritesDpopBoundAccessTokens_AndTheOptionalNames()
    {
        var metadata = new OAuthClientMetadata
        {
            ClientId = "https://app.example.com/c.json",
            TokenEndpointAuthSigningAlg = "ES256",
            JwksUri = "https://app.example.com/jwks.json",
        };
        var json = metadata.ToJson(indented: false);

        Assert.Equal(
            "{\"client_id\":\"https://app.example.com/c.json\",\"redirect_uris\":[],\"token_endpoint_auth_signing_alg\":\"ES256\",\"jwks_uri\":\"https://app.example.com/jwks.json\",\"dpop_bound_access_tokens\":false}",
            json);
        Assert.Equal(json, JsonSerializer.Serialize(metadata, OAuthClientMetadata.JsonTypeInfo));
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var metadata = Hosted(redirects: new[] { "com.example.app:/callback", "https://app.example.com/oauth/callback" });
        var json = metadata.ToJson();
        var parsed = OAuthClientMetadata.FromJson(json)!;

        Assert.Equal(metadata.ClientId, parsed.ClientId);
        Assert.Equal(metadata.RedirectUris, parsed.RedirectUris);
        Assert.Equal(metadata.ResponseTypes, parsed.ResponseTypes);
        Assert.Equal(metadata.GrantTypes, parsed.GrantTypes);
        Assert.Equal(metadata.Scope, parsed.Scope);
        Assert.Equal(metadata.TokenEndpointAuthMethod, parsed.TokenEndpointAuthMethod);
        Assert.Equal(metadata.ApplicationType, parsed.ApplicationType);
        Assert.Equal(metadata.ClientName, parsed.ClientName);
        Assert.Equal(metadata.ClientUri, parsed.ClientUri);
        Assert.Equal(metadata.LogoUri, parsed.LogoUri);
        Assert.Equal(metadata.TosUri, parsed.TosUri);
        Assert.Equal(metadata.PolicyUri, parsed.PolicyUri);
        Assert.True(parsed.DpopBoundAccessTokens);
        Assert.Equal(json, parsed.ToJson());
        parsed.Validate();
        Assert.Null(OAuthClientMetadata.FromJson("null"));
    }

    // --- Accepted documents ---

    [Fact]
    public void HostedMetadata_HasTheRequiredFields_AndPassesTheRules()
    {
        var metadata = Hosted(redirects: new[] { "com.example.app:/callback", "https://app.example.com/oauth/callback" });
        using var doc = JsonDocument.Parse(metadata.ToJson());
        var root = doc.RootElement;

        Assert.Equal("https://app.example.com/oauth-client-metadata.json", root.GetProperty("client_id").GetString());
        Assert.Equal("none", root.GetProperty("token_endpoint_auth_method").GetString());
        Assert.True(root.GetProperty("dpop_bound_access_tokens").GetBoolean());
        Assert.Equal(new[] { "authorization_code", "refresh_token" }, root.GetProperty("grant_types").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new[] { "code" }, root.GetProperty("response_types").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new[] { "com.example.app:/callback", "https://app.example.com/oauth/callback" }, root.GetProperty("redirect_uris").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(ExpectedScope, root.GetProperty("scope").GetString());
        Assert.Equal("native", root.GetProperty("application_type").GetString());
        Assert.Equal("SocialApp", root.GetProperty("client_name").GetString());
        Assert.Equal("https://app.example.com", root.GetProperty("client_uri").GetString());
        Assert.Equal("https://app.example.com/tos", root.GetProperty("tos_uri").GetString());
        Assert.Equal("https://app.example.com/privacy", root.GetProperty("policy_uri").GetString());
        Assert.False(root.TryGetProperty("token_endpoint_auth_signing_alg", out _));

        Assert.Null(OAuthClientMetadataValidator.Check(OAuthClientMetadata.FromJson(metadata.ToJson())!));
        metadata.Validate();
    }

    public static TheoryData<OAuthClientMetadata> ValidMetadata() => new()
    {
        Hosted(),
        Hosted(clientUri: null),
        Hosted(clientUri: "https://app.example.com/"),
        Hosted(clientId: "https://app.example.com/oauth/client.json", clientUri: "https://app.example.com/oauth"),
        Hosted(clientId: "https://app.example.com/oauth/client.json", clientUri: "https://app.example.com/oauth/"),
        Hosted(clientId: "https://app.example.com/oauth/client.json", clientUri: "https://app.example.com/oauth/client.json"),
        Hosted(clientId: "https://app.example.com:8443/client.json", clientUri: "https://app.example.com:8443/"),
        Hosted(redirects: new[] { "http://127.0.0.1/callback", "http://127.0.0.1:8080/cb", "http://[::1]/callback", "http://[::1]:1234/" }),
        Hosted(redirects: new[] { "https://app.example.com/cb" }, applicationType: OAuthApplicationType.Web),
        Hosted(scope: "atproto"),
        Hosted(scope: "atproto transition:generic transition:chat.bsky some:unknown-value"),
        OAuthClientMetadata.CreateForLoopbackClientId("http://localhost"),
        OAuthClientMetadata.CreateForLoopbackClientId("http://localhost/?scope=atproto+transition%3Ageneric&redirect_uri=http%3A%2F%2F%5B%3A%3A1%5D%3A80%2Fcb"),
        MetroSocialNative(),
        MetroSocialWeb(),
    };

    [Theory]
    [MemberData(nameof(ValidMetadata))]
    public void Validator_AcceptsValidMetadata(OAuthClientMetadata metadata)
    {
        Assert.Null(OAuthClientMetadataValidator.Check(metadata));
        OAuthClientMetadataValidator.Validate(metadata);
    }

    [Fact]
    public void Validator_AcceptsAConfidentialWebClient()
    {
        var metadata = Hosted(redirects: new[] { "https://app.example.com/cb" }, applicationType: OAuthApplicationType.Web);
        metadata.TokenEndpointAuthMethod = "private_key_jwt";
        metadata.TokenEndpointAuthSigningAlg = "ES256";
        metadata.JwksUri = "https://app.example.com/jwks.json";
        Assert.Null(OAuthClientMetadataValidator.Check(metadata));
    }

    // --- Rejected documents ---

    public static TheoryData<string, OAuthClientMetadata> InvalidMetadata() => new()
    {
        { "At least one redirect_uri is required", With(d => d.RedirectUris = Array.Empty<string>()) },
        { "ClientID must contain a path component (e.g. \"/client-metadata.json\")", With(d => { d.ClientId = "https://app.example.com/"; d.ClientUri = null; }) },
        { "ClientID must contain a path component (e.g. \"/client-metadata.json\")", With(d => { d.ClientId = "https://app.example.com"; d.ClientUri = null; }) },
        { "ClientID path must not end with a trailing slash", With(d => { d.ClientId = "https://app.example.com/meta/"; d.ClientUri = null; }) },
        { "ClientID hostname must not be an IP address", With(d => { d.ClientId = "https://1.2.3.4/client.json"; d.ClientUri = null; }) },
        { "ClientID hostname must not be an IP address", With(d => { d.ClientId = "https://[2001:db8::1]/client.json"; d.ClientUri = null; }) },
        { "ClientID must not contain a fragment", With(d => d.ClientId = "https://app.example.com/client.json#x") },
        { "ClientID must not contain credentials", With(d => d.ClientId = "https://user:pass@app.example.com/client.json") },
        { "ClientID must be in canonical form (\"https://app.example.com/client.json\", got \"https://app.example.com/a/../client.json\")", With(d => d.ClientId = "https://app.example.com/a/../client.json") },
        { "URL must use the \"https:\" protocol", With(d => d.ClientId = "ftp://app.example.com/client.json") },
        { "https: URL must not use a loopback host", With(d => { d.ClientId = "https://127.0.0.1/client.json"; d.ClientUri = null; }) },
        { "Domain name must contain at least two segments", With(d => { d.ClientId = "https://intranet/client.json"; d.ClientUri = null; }) },
        { "Domain name must not end with \".local\"", With(d => { d.ClientId = "https://app.local/client.json"; d.ClientUri = null; }) },
        { "Client metadata must include the \"atproto\" scope", With(d => d.Scope = "transition:generic") },
        { "Client metadata must include the \"atproto\" scope", With(d => d.Scope = null) },
        { "Invalid OAuth scope", With(d => d.Scope = "atproto  blob:*/*") },
        { "Invalid OAuth scope", With(d => d.Scope = "atproto \"quoted\"") },
        { "Duplicate scope \"atproto\"", With(d => d.Scope = "atproto atproto") },
        { "Invalid scope \"rpc:app.bsky.video.getUploadLimits?aud=did:web:video.bsky.app\"", With(d => d.Scope = "atproto rpc:app.bsky.video.getUploadLimits?aud=did:web:video.bsky.app") },
        { "Invalid response_types", With(d => d.ResponseTypes = new[] { "code", "bogus" }) },
        { "Invalid grant_types", With(d => d.GrantTypes = new[] { "authorization_code", "bogus" }) },
        { "\"response_types\" must include \"code\"", With(d => d.ResponseTypes = new[] { "token" }) },
        { "\"grant_types\" must include \"authorization_code\"", With(d => d.GrantTypes = new[] { "refresh_token" }) },
        { "Grant type \"implicit\" is not allowed", With(d => d.GrantTypes = new[] { "authorization_code", "implicit" }) },
        { "Grant type \"client_credentials\" is not supported", With(d => d.GrantTypes = new[] { "authorization_code", "client_credentials" }) },
        { "Duplicate grant type \"authorization_code\"", With(d => d.GrantTypes = new[] { "authorization_code", "authorization_code" }) },
        { "\"token_endpoint_auth_signing_alg\" must not be provided when \"token_endpoint_auth_method\" is \"none\"", With(d => d.TokenEndpointAuthSigningAlg = "ES256") },
        { "\"dpop_bound_access_tokens\" must be true", With(d => d.DpopBoundAccessTokens = false) },
        { "application_type must be \"web\" or \"native\"", With(d => d.ApplicationType = "desktop") },
        { "client_uri: URL must use the \"http:\" or \"https:\" protocol", With(d => d.ClientUri = "ftp://app.example.com/") },
        { "tos_uri: URL must use \"localhost\", \"127.0.0.1\" or \"[::1]\" as hostname", With(d => d.TosUri = "http://app.example.com/tos") },
        { "Use of \"localhost\" hostname is not allowed (RFC 8252), use a loopback IP such as \"127.0.0.1\" instead", With(d => d.RedirectUris = new[] { "http://localhost:8080/cb" }) },
        { "URL must use \"localhost\", \"127.0.0.1\" or \"[::1]\" as hostname", With(d => d.RedirectUris = new[] { "http://app.example.com/cb" }) },
        { "Loopback redirect URIs are only allowed for native apps", With(d => { d.ApplicationType = "web"; d.RedirectUris = new[] { "http://127.0.0.1:8080/cb" }; }) },
        { "Loopback redirect URIs are only allowed for native apps", With(d => { d.ApplicationType = "web"; d.RedirectUris = new[] { "http://[::1]/cb" }; }) },
        { "Private-Use URI Scheme redirect URI are only allowed for native apps", With(d => d.ApplicationType = "web") },
        { "Private-Use URI Scheme must be in the form <scheme>:/{path} (notice the single slash!) as per RFC 8252", With(d => d.RedirectUris = new[] { "com.example.app://callback" }) },
        { "Private-Use URI Scheme must be in the form <scheme>:/{path} (notice the single slash!) as per RFC 8252", With(d => d.RedirectUris = new[] { "com.example.app:///callback" }) },
        { "Private-use URI Scheme redirect URI must not be a local hostname", With(d => d.RedirectUris = new[] { "test.app:/callback" }) },
        { "Private-Use URI Scheme redirect URI, for discoverable client metadata, must be the fully qualified domain name (FQDN) of the client_id, in reverse order (com.example.app:)", With(d => d.RedirectUris = new[] { "com.other.app:/callback" }) },
        { "Private-Use URI Scheme redirect URI, for discoverable client metadata, must be the fully qualified domain name (FQDN) of the client_id, in reverse order (vip.drasticactions:)", With(d => { d.ClientId = "https://drasticactions.vip/metrosocial/client-metadata.json"; d.ClientUri = null; d.RedirectUris = new[] { "vip.drasticactions.metrosocial:/callback" }; }) },
        { "Redirect URI \"https://app.test/cb\"'s domain name must not be a local hostname", With(d => d.RedirectUris = new[] { "https://app.test/cb" }) },
        { "Redirect URI \"https://app.localhost/cb\"'s domain name must not be a local hostname", With(d => d.RedirectUris = new[] { "https://app.localhost/cb" }) },
        { "Redirect URI \"https://app.invalid/cb\"'s domain name must not be a local hostname", With(d => d.RedirectUris = new[] { "https://app.invalid/cb" }) },
        { "Redirect URI \"https://app.example/cb\"'s domain name must not be a local hostname", With(d => d.RedirectUris = new[] { "https://app.example/cb" }) },
        { "Domain name must not end with \".local\"", With(d => d.RedirectUris = new[] { "https://app.local/cb" }) },
        { "Redirect URI https://user@app.example.com/cb must not contain credentials", With(d => d.RedirectUris = new[] { "https://user@app.example.com/cb" }) },
        { "URL must use the \"https:\" or \"http:\" protocol, or a private-use URI scheme (RFC 8252)", With(d => d.RedirectUris = new[] { "myapp:/callback" }) },
        { "client_uri must have the same origin as the client_id", With(d => d.ClientUri = "https://other.example.com") },
        { "client_uri must have the same origin as the client_id", With(d => d.ClientUri = "https://app.example.com:8443/") },
        { "client_uri must be a parent URL of the client_id", With(d => d.ClientUri = "https://app.example.com/somewhere") },
        { "client_uri must be a parent URL of the client_id", With(d => { d.ClientId = "https://app.example.com/oauthx/client.json"; d.ClientUri = "https://app.example.com/oauth"; }) },
        { "client_uri hostname is invalid", With(d => d.ClientUri = "https://app.example") },
        { "client_uri is not allowed for loopback clients", With(d => { d.ClientId = "http://localhost"; d.RedirectUris = new[] { "http://127.0.0.1/" }; d.Scope = "atproto"; }) },
        { "Loopback clients must have application_type \"native\"", With(d => { d.ClientId = "http://localhost"; d.RedirectUris = new[] { "https://app.example.com/cb" }; d.Scope = "atproto"; d.ApplicationType = "web"; d.ClientUri = null; }) },
        { "Invalid loopback client ID: Value must not contain a path component", With(d => { d.ClientId = "http://localhost/path"; d.ClientUri = null; }) },
        { "\"token_endpoint_auth_signing_alg\" must be provided when \"token_endpoint_auth_method\" is \"private_key_jwt\"", With(d => d.TokenEndpointAuthMethod = "private_key_jwt") },
        { "Client authentication method \"private_key_jwt\" requires a JWKS", With(d => { d.TokenEndpointAuthMethod = "private_key_jwt"; d.TokenEndpointAuthSigningAlg = "ES256"; }) },
        { "Native clients must authenticate using \"none\" method", With(d => { d.TokenEndpointAuthMethod = "private_key_jwt"; d.TokenEndpointAuthSigningAlg = "ES256"; d.JwksUri = "https://app.example.com/jwks.json"; }) },
        { "jwks_uri and jwks are mutually exclusive", With(d => { d.TokenEndpointAuthMethod = "private_key_jwt"; d.TokenEndpointAuthSigningAlg = "ES256"; d.JwksUri = "https://app.example.com/jwks.json"; d.Jwks = new JsonWebKeySet(); }) },
        { "Unsupported \"token_endpoint_auth_method\" value: client_secret_post", With(d => d.TokenEndpointAuthMethod = "client_secret_post") },
        { "Unsupported \"token_endpoint_auth_method\" value: client_secret_basic", With(d => d.TokenEndpointAuthMethod = null) },
        { "client_id is required", With(d => d.ClientId = string.Empty) },
    };

    [Theory]
    [MemberData(nameof(InvalidMetadata))]
    public void Validator_RejectsMetadataBreakingTheAtprotoRules(string expected, OAuthClientMetadata metadata)
    {
        Assert.Equal(expected, OAuthClientMetadataValidator.Check(metadata));
        var ex = Assert.Throws<InvalidOAuthClientMetadataException>(() => OAuthClientMetadataValidator.Validate(metadata));
        Assert.Equal(expected, ex.Message);
        Assert.Throws<InvalidOAuthClientMetadataException>(metadata.Validate);
        Assert.False(metadata.TryValidate(out var error));
        Assert.Equal(expected, error);
    }

    // --- Scope rules ---

    [Theory]
    [InlineData("blob:*/* account:email", "Missing \"atproto\" scope")]
    [InlineData("atproto  account:email", "Invalid OAuth scope")]
    [InlineData("atproto account:email account:email", "Duplicate scope \"account:email\"")]
    [InlineData("atproto account:nonsense", "Invalid scope \"account:nonsense\"")]
    [InlineData("atproto identity:nonsense", "Invalid scope \"identity:nonsense\"")]
    [InlineData("atproto include:", "Invalid scope \"include:\"")]
    [InlineData("atproto blob:notamime", "Invalid scope \"blob:notamime\"")]
    // A PDS ignores an rpc: permission whose audience is a bare DID (only "*" or did#service parse).
    [InlineData("atproto rpc:app.bsky.video.getUploadLimits?aud=did:web:video.bsky.app", "Invalid scope \"rpc:app.bsky.video.getUploadLimits?aud=did:web:video.bsky.app\"")]
    [InlineData("", "Missing scope property")]
    [InlineData(null, "Missing scope property")]
    public void CheckScope_RejectsMalformedScopes(string? scope, string expected)
    {
        Assert.Equal(expected, OAuthClientMetadataValidator.CheckScope(scope));

        // The document rules reject them too (the message may name an earlier rule).
        Assert.Throws<InvalidOAuthClientMetadataException>(() => With(d => d.Scope = scope).Validate());
    }

    [Theory]
    [InlineData("atproto")]
    [InlineData("atproto transition:generic")]
    [InlineData("atproto repo:app.bsky.feed.post?action=create rpc:app.bsky.actor.getProfile?aud=did:web:api.bsky.app%23bsky_appview")]
    [InlineData("atproto some:future-scope")]
    [InlineData(ExpectedScope)]
    public void CheckScope_AcceptsValidScopes(string scope)
    {
        Assert.Null(OAuthClientMetadataValidator.CheckScope(scope));
        Assert.Null(With(d => d.Scope = scope).TryValidate(out var error) ? null : error);
    }

    // --- Loopback client ID rules ---

    [Theory]
    [InlineData(null, null, "http://localhost")]
    [InlineData("atproto", null, "http://localhost")]
    [InlineData("atproto transition:generic", null, "http://localhost?scope=atproto+transition%3Ageneric")]
    [InlineData(null, "http://127.0.0.1/", "http://localhost")]
    [InlineData(null, "http://127.0.0.1:1234/cb", "http://localhost?redirect_uri=http%3A%2F%2F127.0.0.1%3A1234%2Fcb")]
    public void LoopbackClientId_Build(string? scope, string? redirect, string expected)
    {
        IEnumerable<string>? redirects = redirect switch
        {
            null => null,
            "http://127.0.0.1/" => new[] { "http://127.0.0.1/", "http://[::1]/" },
            _ => new[] { redirect },
        };
        Assert.Equal(expected, AtprotoLoopbackClientId.Build(scope, redirects));
        Assert.Null(OAuthClientMetadataValidator.Check(OAuthClientMetadata.CreateForLoopbackClientId(expected)));
    }

    [Theory]
    [InlineData("http://localhost", null)]
    [InlineData("http://localhost/", null)]
    [InlineData("http://localhost?scope=atproto", null)]
    [InlineData("http://127.0.0.1", "Value must start with \"http://localhost\"")]
    [InlineData("http://localhost#x", "Value must not contain a hash component")]
    [InlineData("http://localhost/path", "Value must not contain a path component")]
    [InlineData("http://localhost?foo=bar", "Unexpected query parameter \"foo\"")]
    [InlineData("http://localhost?scope=atproto&scope=atproto", "Duplicate \"scope\" query parameter")]
    [InlineData("http://localhost?scope=transition%3Ageneric", "ATProto Loopback ClientID must include \"atproto\" scope")]
    [InlineData("http://localhost?redirect_uri=http%3A%2F%2Flocalhost%2Fcb", "Invalid \"redirect_uri\" query parameter: Use of \"localhost\" hostname is not allowed (RFC 8252), use a loopback IP such as \"127.0.0.1\" instead")]
    public void LoopbackClientId_Parse(string clientId, string? expectedError)
    {
        var result = AtprotoLoopbackClientId.TryParse(clientId, out var error);
        Assert.Equal(expectedError, error);
        Assert.Equal(expectedError is null, result is not null);

        var metadata = Hosted(clientId: clientId, redirects: new[] { "http://127.0.0.1/" }, scope: "atproto", clientUri: null);
        Assert.Equal(expectedError is null ? null : $"Invalid loopback client ID: {expectedError}", OAuthClientMetadataValidator.Check(metadata));
    }

    [Fact]
    public void LoopbackDocument_FromAConfig_PassesTheRules()
    {
        var config = OAuthClientConfig.CreateLoopback(8080, ExpectedScope);
        var metadata = OAuthClientMetadata.CreateForLoopbackClientId(config.ClientId);

        Assert.Equal(new[] { config.RedirectUri }, metadata.RedirectUris);
        Assert.Equal(ExpectedScope, metadata.Scope);
        metadata.Validate();
    }
}
