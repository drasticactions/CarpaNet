using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CarpaNet.OAuth.Scopes;

namespace CarpaNet.OAuth;

/// <summary>
/// OAuth 2.0 Client Metadata (RFC 7591 names): the document a client hosts at its client ID URL.
/// </summary>
/// <example>
/// <code>
/// var metadata = OAuthClientMetadata.CreatePublicClient(
///     "https://app.example.com/client-metadata.json",
///     new[] { "com.example.app:/callback" },
///     new ScopeSet().AddAtproto().AddTransitionGeneric());
/// metadata.ClientName = "Example";
/// metadata.ClientUri = "https://app.example.com/";
/// metadata.Validate();
/// var json = metadata.ToJson();
/// </code>
/// </example>
public sealed class OAuthClientMetadata
{
    private static readonly OAuthJsonContext IndentedContext = new(new JsonSerializerOptions(OAuthJsonContext.Default.Options) { WriteIndented = true });

    /// <summary>
    /// Gets the source-generated JSON type info for this document (no reflection; AOT and trim safe),
    /// for example for ASP.NET Core's <c>Results.Json(metadata, OAuthClientMetadata.JsonTypeInfo)</c>.
    /// Null properties are omitted.
    /// </summary>
    public static JsonTypeInfo<OAuthClientMetadata> JsonTypeInfo => OAuthJsonContext.Default.OAuthClientMetadata;

    /// <summary>
    /// The client identifier.
    /// </summary>
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Array of redirect URIs.
    /// </summary>
    [JsonPropertyName("redirect_uris")]
    public string[] RedirectUris { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Response types the client uses.
    /// </summary>
    [JsonPropertyName("response_types")]
    public string[]? ResponseTypes { get; set; }

    /// <summary>
    /// Grant types the client uses.
    /// </summary>
    [JsonPropertyName("grant_types")]
    public string[]? GrantTypes { get; set; }

    /// <summary>
    /// Requested scope.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>
    /// Token endpoint authentication method.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; set; }

    /// <summary>
    /// Token endpoint authentication signing algorithm.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_signing_alg")]
    public string? TokenEndpointAuthSigningAlg { get; set; }

    /// <summary>
    /// JWK Set containing client's public keys.
    /// </summary>
    [JsonPropertyName("jwks")]
    public JsonWebKeySet? Jwks { get; set; }

    /// <summary>
    /// URL of the client's JWK Set.
    /// </summary>
    [JsonPropertyName("jwks_uri")]
    public string? JwksUri { get; set; }

    /// <summary>
    /// Application type (web or native).
    /// </summary>
    [JsonPropertyName("application_type")]
    public string? ApplicationType { get; set; }

    /// <summary>
    /// Human-readable client name.
    /// </summary>
    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    /// <summary>
    /// URL of the client's home page.
    /// </summary>
    [JsonPropertyName("client_uri")]
    public string? ClientUri { get; set; }

    /// <summary>
    /// URL of the client's logo.
    /// </summary>
    [JsonPropertyName("logo_uri")]
    public string? LogoUri { get; set; }

    /// <summary>
    /// URL of the client's terms of service.
    /// </summary>
    [JsonPropertyName("tos_uri")]
    public string? TosUri { get; set; }

    /// <summary>
    /// URL of the client's privacy policy.
    /// </summary>
    [JsonPropertyName("policy_uri")]
    public string? PolicyUri { get; set; }

    /// <summary>
    /// Whether access tokens must be DPoP-bound.
    /// </summary>
    [JsonPropertyName("dpop_bound_access_tokens")]
    public bool DpopBoundAccessTokens { get; set; }

    /// <summary>
    /// Creates the metadata of an atproto public client (<c>token_endpoint_auth_method: none</c>):
    /// the <c>authorization_code</c> and <c>refresh_token</c> grants, the <c>code</c> response type and
    /// DPoP-bound access tokens. Set <see cref="ClientName"/>, <see cref="ClientUri"/>, <see cref="LogoUri"/>,
    /// <see cref="TosUri"/> and <see cref="PolicyUri"/> afterwards, then call <see cref="Validate"/>.
    /// </summary>
    /// <param name="clientId">The client ID: the https URL this document is served at.</param>
    /// <param name="redirectUris">The redirect URIs.</param>
    /// <param name="scope">The space-separated scope (must contain <c>atproto</c>).</param>
    /// <param name="applicationType"><see cref="OAuthApplicationType.Native"/> or <see cref="OAuthApplicationType.Web"/>.</param>
    /// <returns>The metadata.</returns>
    public static OAuthClientMetadata CreatePublicClient(
        string clientId,
        IEnumerable<string> redirectUris,
        string scope,
        string applicationType = OAuthApplicationType.Native)
    {
        if (clientId == null)
        {
            throw new ArgumentNullException(nameof(clientId));
        }

        if (redirectUris == null)
        {
            throw new ArgumentNullException(nameof(redirectUris));
        }

        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (applicationType == null)
        {
            throw new ArgumentNullException(nameof(applicationType));
        }

        return new OAuthClientMetadata
        {
            ClientId = clientId,
            RedirectUris = redirectUris.ToArray(),
            Scope = scope,
            ApplicationType = applicationType,
            GrantTypes = new[] { "authorization_code", "refresh_token" },
            ResponseTypes = new[] { "code" },
            TokenEndpointAuthMethod = "none",
            DpopBoundAccessTokens = true,
        };
    }

    /// <summary>
    /// Creates the metadata of an atproto public client, with the scope from a <see cref="ScopeSet"/>.
    /// </summary>
    /// <param name="clientId">The client ID: the https URL this document is served at.</param>
    /// <param name="redirectUris">The redirect URIs.</param>
    /// <param name="scope">The scope (must contain <c>atproto</c>).</param>
    /// <param name="applicationType"><see cref="OAuthApplicationType.Native"/> or <see cref="OAuthApplicationType.Web"/>.</param>
    /// <returns>The metadata.</returns>
    public static OAuthClientMetadata CreatePublicClient(
        string clientId,
        IEnumerable<string> redirectUris,
        ScopeSet scope,
        string applicationType = OAuthApplicationType.Native)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        return CreatePublicClient(clientId, redirectUris, scope.ToString(), applicationType);
    }

    /// <summary>
    /// Creates the metadata an authorization server derives from an atproto loopback client ID
    /// (<c>atprotoLoopbackClientMetadata</c>): nothing is hosted for such a client. The scope and redirect
    /// URIs come from the ID's query parameters (defaults <c>atproto</c> and
    /// <c>http://127.0.0.1/</c>, <c>http://[::1]/</c>).
    /// </summary>
    /// <param name="loopbackClientId">The loopback client ID (see <see cref="AtprotoLoopbackClientId.Build"/>).</param>
    /// <returns>The metadata.</returns>
    /// <exception cref="ArgumentException">The ID is not a valid atproto loopback client ID.</exception>
    public static OAuthClientMetadata CreateForLoopbackClientId(string loopbackClientId)
    {
        if (loopbackClientId == null)
        {
            throw new ArgumentNullException(nameof(loopbackClientId));
        }

        var parameters = AtprotoLoopbackClientId.TryParse(loopbackClientId, out var error)
            ?? throw new ArgumentException($"Invalid loopback client ID: {error}", nameof(loopbackClientId));

        return new OAuthClientMetadata
        {
            ClientId = loopbackClientId,
            Scope = parameters.Scope,
            RedirectUris = parameters.RedirectUris.ToArray(),
            ResponseTypes = new[] { "code" },
            GrantTypes = new[] { "authorization_code", "refresh_token" },
            TokenEndpointAuthMethod = "none",
            ApplicationType = OAuthApplicationType.Native,
            DpopBoundAccessTokens = true,
        };
    }

    /// <summary>
    /// Reads a metadata document.
    /// </summary>
    /// <param name="json">The JSON document.</param>
    /// <returns>The metadata, or null when the JSON is <c>null</c>.</returns>
    /// <exception cref="JsonException">The JSON is invalid.</exception>
    public static OAuthClientMetadata? FromJson(string json)
    {
        if (json == null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        return JsonSerializer.Deserialize(json, OAuthJsonContext.Default.OAuthClientMetadata);
    }

    /// <summary>
    /// Writes this document as JSON with the RFC 7591 property names. Null properties are omitted;
    /// <c>dpop_bound_access_tokens</c> is always written.
    /// </summary>
    /// <param name="indented">Whether to indent the JSON.</param>
    /// <returns>The JSON document.</returns>
    public string ToJson(bool indented = true) =>
        JsonSerializer.Serialize(this, indented ? IndentedContext.OAuthClientMetadata : OAuthJsonContext.Default.OAuthClientMetadata);

    /// <summary>
    /// Validates this document against the atproto client metadata rules (see <see cref="OAuthClientMetadataValidator"/>).
    /// </summary>
    /// <exception cref="InvalidOAuthClientMetadataException">The first broken rule.</exception>
    public void Validate() => OAuthClientMetadataValidator.Validate(this);

    /// <summary>
    /// Validates this document against the atproto client metadata rules (see <see cref="OAuthClientMetadataValidator"/>).
    /// </summary>
    /// <param name="error">The first broken rule, or null.</param>
    /// <returns>True when the document is valid.</returns>
    public bool TryValidate(out string? error)
    {
        error = OAuthClientMetadataValidator.Check(this);
        return error == null;
    }
}

/// <summary>
/// JSON Web Key Set.
/// </summary>
public sealed class JsonWebKeySet
{
    /// <summary>
    /// Array of JSON Web Keys.
    /// </summary>
    [JsonPropertyName("keys")]
    public JsonWebKey[] Keys { get; set; } = Array.Empty<JsonWebKey>();
}

/// <summary>
/// JSON Web Key (RFC 7517).
/// </summary>
public sealed class JsonWebKey
{
    /// <summary>
    /// Key type (e.g., "EC", "RSA", "OKP").
    /// </summary>
    [JsonPropertyName("kty")]
    public string Kty { get; set; } = string.Empty;

    /// <summary>
    /// Key ID.
    /// </summary>
    [JsonPropertyName("kid")]
    public string? Kid { get; set; }

    /// <summary>
    /// Intended use (e.g., "sig", "enc").
    /// </summary>
    [JsonPropertyName("use")]
    public string? Use { get; set; }

    /// <summary>
    /// Key operations.
    /// </summary>
    [JsonPropertyName("key_ops")]
    public string[]? KeyOps { get; set; }

    /// <summary>
    /// Algorithm.
    /// </summary>
    [JsonPropertyName("alg")]
    public string? Alg { get; set; }

    /// <summary>
    /// Curve (for EC and OKP keys).
    /// </summary>
    [JsonPropertyName("crv")]
    public string? Crv { get; set; }

    /// <summary>
    /// X coordinate (for EC keys).
    /// </summary>
    [JsonPropertyName("x")]
    public string? X { get; set; }

    /// <summary>
    /// Y coordinate (for EC keys).
    /// </summary>
    [JsonPropertyName("y")]
    public string? Y { get; set; }

    /// <summary>
    /// Private key value (for EC keys). Only present in private keys.
    /// </summary>
    [JsonPropertyName("d")]
    public string? D { get; set; }

    /// <summary>
    /// RSA modulus.
    /// </summary>
    [JsonPropertyName("n")]
    public string? N { get; set; }

    /// <summary>
    /// RSA public exponent.
    /// </summary>
    [JsonPropertyName("e")]
    public string? E { get; set; }
}
