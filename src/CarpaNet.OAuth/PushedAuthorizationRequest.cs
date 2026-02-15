using System.Text.Json.Serialization;

namespace CarpaNet.OAuth;

/// <summary>
/// Pushed Authorization Request response (RFC 9126).
/// </summary>
public sealed class PushedAuthorizationResponse
{
    /// <summary>
    /// The request URI to use in the authorization request.
    /// </summary>
    [JsonPropertyName("request_uri")]
    public string RequestUri { get; set; } = string.Empty;

    /// <summary>
    /// The lifetime in seconds of the request URI.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

/// <summary>
/// OAuth error response.
/// </summary>
public sealed class OAuthErrorResponse
{
    /// <summary>
    /// The error code.
    /// </summary>
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable error description.
    /// </summary>
    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }

    /// <summary>
    /// URI with more information about the error.
    /// </summary>
    [JsonPropertyName("error_uri")]
    public string? ErrorUri { get; set; }
}
