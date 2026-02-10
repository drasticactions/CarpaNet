using System;
using System.Text.Json.Serialization;

namespace CarpaNet.OAuth;

/// <summary>
/// OAuth 2.0 Client Metadata.
/// </summary>
public sealed class OAuthClientMetadata
{
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
    /// Whether access tokens must be DPoP-bound.
    /// </summary>
    [JsonPropertyName("dpop_bound_access_tokens")]
    public bool DpopBoundAccessTokens { get; set; }
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
