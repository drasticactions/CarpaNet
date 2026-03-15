using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CarpaNet.OAuth.Crypto;

/// <summary>
/// Creates client assertion JWTs for private_key_jwt authentication (RFC 7523).
/// </summary>
public static class ClientAssertion
{
    /// <summary>
    /// The client assertion type URN.
    /// </summary>
    public const string AssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    /// <summary>
    /// Creates a client assertion JWT.
    /// </summary>
    /// <param name="clientId">The client ID.</param>
    /// <param name="audience">The token endpoint URL or issuer.</param>
    /// <param name="keyPair">The signing key pair.</param>
    /// <param name="keyId">Optional key ID to include in the header.</param>
    /// <returns>A signed JWT for client authentication.</returns>
    /// <remarks>
    /// On browser platforms, use <see cref="CreateAsync"/> instead.
    /// </remarks>
    public static string Create(string clientId, string audience, DPoPKeyPair keyPair, string? keyId = null)
    {
        var (headerBase64, payloadBase64) = BuildComponents(clientId, audience, keyPair, keyId);
        var signingInput = $"{headerBase64}.{payloadBase64}";

        var signature = keyPair.SignData(Encoding.UTF8.GetBytes(signingInput));
        var signatureBase64 = Pkce.Base64UrlEncode(signature);

        return $"{signingInput}.{signatureBase64}";
    }

    /// <summary>
    /// Creates a client assertion JWT asynchronously. Works on all platforms including browser.
    /// </summary>
    /// <param name="clientId">The client ID.</param>
    /// <param name="audience">The token endpoint URL or issuer.</param>
    /// <param name="keyPair">The signing key pair.</param>
    /// <param name="keyId">Optional key ID to include in the header.</param>
    /// <returns>A signed JWT for client authentication.</returns>
    public static async Task<string> CreateAsync(string clientId, string audience, DPoPKeyPair keyPair, string? keyId = null)
    {
        var (headerBase64, payloadBase64) = BuildComponents(clientId, audience, keyPair, keyId);
        var signingInput = $"{headerBase64}.{payloadBase64}";

        var signature = await keyPair.SignDataAsync(Encoding.UTF8.GetBytes(signingInput)).ConfigureAwait(false);
        var signatureBase64 = Pkce.Base64UrlEncode(signature);

        return $"{signingInput}.{signatureBase64}";
    }

    private static (string HeaderBase64, string PayloadBase64) BuildComponents(
        string clientId, string audience, DPoPKeyPair keyPair, string? keyId)
    {
        var now = DateTimeOffset.UtcNow;

        // Build header
        var header = new JwtHeader
        {
            Alg = keyPair.Algorithm,
            Typ = "JWT",
            Kid = string.IsNullOrEmpty(keyId) ? null : keyId
        };

        // Build payload
        var payload = new ClientAssertionPayload
        {
            Iss = clientId,
            Sub = clientId,
            Aud = audience,
            Jti = Pkce.GenerateNonce(16),
            Iat = now.ToUnixTimeSeconds(),
            Exp = now.AddMinutes(1).ToUnixTimeSeconds()
        };

        var headerJson = JsonSerializer.Serialize(header, OAuthJsonContext.Default.JwtHeader);
        var payloadJson = JsonSerializer.Serialize(payload, OAuthJsonContext.Default.ClientAssertionPayload);

        var headerBase64 = Pkce.Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadBase64 = Pkce.Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        return (headerBase64, payloadBase64);
    }
}
