using System;
using System.Text.Json.Serialization;

namespace CarpaNet.OAuth;

/// <summary>
/// OAuth 2.0 Authorization Server Metadata (RFC 8414).
/// </summary>
public sealed class OAuthAuthorizationServerMetadata
{
    /// <summary>
    /// The authorization server's issuer identifier (URL).
    /// </summary>
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// URL of the authorization endpoint.
    /// </summary>
    [JsonPropertyName("authorization_endpoint")]
    public string AuthorizationEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// URL of the token endpoint.
    /// </summary>
    [JsonPropertyName("token_endpoint")]
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// URL of the server's JWK Set document.
    /// </summary>
    [JsonPropertyName("jwks_uri")]
    public string? JwksUri { get; set; }

    /// <summary>
    /// URL of the pushed authorization request endpoint.
    /// </summary>
    [JsonPropertyName("pushed_authorization_request_endpoint")]
    public string? PushedAuthorizationRequestEndpoint { get; set; }

    /// <summary>
    /// Whether the server requires pushed authorization requests.
    /// </summary>
    [JsonPropertyName("require_pushed_authorization_requests")]
    public bool RequirePushedAuthorizationRequests { get; set; }

    /// <summary>
    /// DPoP signing algorithms supported by the server.
    /// </summary>
    [JsonPropertyName("dpop_signing_alg_values_supported")]
    public string[]? DpopSigningAlgValuesSupported { get; set; }

    /// <summary>
    /// Token endpoint authentication methods supported.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public string[]? TokenEndpointAuthMethodsSupported { get; set; }

    /// <summary>
    /// Token endpoint authentication signing algorithms supported.
    /// </summary>
    [JsonPropertyName("token_endpoint_auth_signing_alg_values_supported")]
    public string[]? TokenEndpointAuthSigningAlgValuesSupported { get; set; }

    /// <summary>
    /// URL of the revocation endpoint.
    /// </summary>
    [JsonPropertyName("revocation_endpoint")]
    public string? RevocationEndpoint { get; set; }

    /// <summary>
    /// URL of the introspection endpoint.
    /// </summary>
    [JsonPropertyName("introspection_endpoint")]
    public string? IntrospectionEndpoint { get; set; }

    /// <summary>
    /// Response types supported.
    /// </summary>
    [JsonPropertyName("response_types_supported")]
    public string[]? ResponseTypesSupported { get; set; }

    /// <summary>
    /// Response modes supported.
    /// </summary>
    [JsonPropertyName("response_modes_supported")]
    public string[]? ResponseModesSupported { get; set; }

    /// <summary>
    /// Whether the authorization response includes the iss parameter.
    /// </summary>
    [JsonPropertyName("authorization_response_iss_parameter_supported")]
    public bool AuthorizationResponseIssParameterSupported { get; set; }

    /// <summary>
    /// Scopes supported by the server.
    /// </summary>
    [JsonPropertyName("scopes_supported")]
    public string[]? ScopesSupported { get; set; }

    /// <summary>
    /// Code challenge methods supported (PKCE).
    /// </summary>
    [JsonPropertyName("code_challenge_methods_supported")]
    public string[]? CodeChallengeMethodsSupported { get; set; }

    /// <summary>
    /// Whether the server supports client ID metadata document discovery.
    /// </summary>
    [JsonPropertyName("client_id_metadata_document_supported")]
    public bool ClientIdMetadataDocumentSupported { get; set; }

    /// <summary>
    /// Protected resources (PDS URLs) associated with this authorization server.
    /// </summary>
    [JsonPropertyName("protected_resources")]
    public string[]? ProtectedResources { get; set; }
}
