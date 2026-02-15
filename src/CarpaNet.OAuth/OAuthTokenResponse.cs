using System;
using System.Text.Json.Serialization;

namespace CarpaNet.OAuth;

/// <summary>
/// OAuth 2.0 Token Response.
/// </summary>
public sealed class OAuthTokenResponse
{
    /// <summary>
    /// The access token.
    /// </summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// The token type (always "DPoP" for ATProto).
    /// </summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "DPoP";

    /// <summary>
    /// The lifetime in seconds of the access token.
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    /// <summary>
    /// The refresh token.
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// The scope of the access token.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>
    /// The subject (user DID) for ATProto tokens.
    /// </summary>
    [JsonPropertyName("sub")]
    public string? Sub { get; set; }
}

/// <summary>
/// Internal token set with additional metadata.
/// </summary>
public sealed class TokenSet
{
    /// <summary>
    /// The authorization server issuer.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// The subject (user DID).
    /// </summary>
    public string Sub { get; set; } = string.Empty;

    /// <summary>
    /// The audience (PDS URL).
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// The granted scope.
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// The access token.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// The refresh token.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// When the access token expires.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Creates a TokenSet from an OAuth token response.
    /// </summary>
    public static TokenSet FromResponse(OAuthTokenResponse response, string issuer, string audience)
    {
        var expiresAt = response.ExpiresIn.HasValue
            ? DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn.Value)
            : (DateTimeOffset?)null;

        return new TokenSet
        {
            Issuer = issuer,
            Sub = response.Sub ?? string.Empty,
            Audience = audience,
            Scope = response.Scope ?? "atproto",
            AccessToken = response.AccessToken,
            RefreshToken = response.RefreshToken,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    /// Whether the token is expired or will expire within the buffer time.
    /// </summary>
    public bool IsExpired(TimeSpan buffer = default)
    {
        if (!ExpiresAt.HasValue)
        {
            return false; // No expiry info, assume valid
        }

        return DateTimeOffset.UtcNow >= ExpiresAt.Value - buffer;
    }
}
