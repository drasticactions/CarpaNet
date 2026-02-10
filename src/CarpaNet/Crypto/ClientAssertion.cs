using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    public static string Create(string clientId, string audience, DPoPKeyPair keyPair, string? keyId = null)
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

        return CreateSignedJwt(header, payload, keyPair);
    }

    private static string CreateSignedJwt(
        JwtHeader header,
        ClientAssertionPayload payload,
        DPoPKeyPair keyPair)
    {
        var headerJson = JsonSerializer.Serialize(header, OAuthJsonContext.Default.JwtHeader);
        var payloadJson = JsonSerializer.Serialize(payload, OAuthJsonContext.Default.ClientAssertionPayload);

        var headerBase64 = Pkce.Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadBase64 = Pkce.Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var signingInput = $"{headerBase64}.{payloadBase64}";

        // Use the key pair's internal signing - we need to create a proof without the DPoP-specific fields
        // For now, we'll create a simple ES256 signature
        var jwk = keyPair.ExportKeyPair();

        using var ecdsa = CreateEcdsaFromJwk(jwk);
        var signature = ecdsa.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256);
        var signatureBase64 = Pkce.Base64UrlEncode(signature);

        return $"{signingInput}.{signatureBase64}";
    }

    private static ECDsa CreateEcdsaFromJwk(JsonWebKey jwk)
    {
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Pkce.Base64UrlDecode(jwk.X!),
                Y = Pkce.Base64UrlDecode(jwk.Y!)
            },
            D = Pkce.Base64UrlDecode(jwk.D!)
        };

        return ECDsa.Create(parameters);
    }
}
