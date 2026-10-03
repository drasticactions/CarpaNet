using System;
using System.Text.Json.Serialization;

namespace CarpaNet.OAuth;

/// <summary>
/// OAuth 2.0 Protected Resource Metadata (RFC 9728), as served by a PDS at
/// <c>/.well-known/oauth-protected-resource</c>.
/// </summary>
public sealed class OAuthProtectedResourceMetadata
{
    /// <summary>
    /// The protected resource's resource identifier (URL).
    /// </summary>
    [JsonPropertyName("resource")]
    public string? Resource { get; set; }

    /// <summary>
    /// Issuer identifiers of the authorization servers that protect this resource.
    /// </summary>
    [JsonPropertyName("authorization_servers")]
    public string[]? AuthorizationServers { get; set; }

    /// <summary>
    /// Scopes supported by the protected resource.
    /// </summary>
    [JsonPropertyName("scopes_supported")]
    public string[]? ScopesSupported { get; set; }

    /// <summary>
    /// Methods supported for sending bearer tokens to the resource.
    /// </summary>
    [JsonPropertyName("bearer_methods_supported")]
    public string[]? BearerMethodsSupported { get; set; }

    /// <summary>
    /// URL of human-readable documentation for the resource.
    /// </summary>
    [JsonPropertyName("resource_documentation")]
    public string? ResourceDocumentation { get; set; }
}
