using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CarpaNet.OAuth.Crypto;

/// <summary>
/// Represents an ES256 key pair for DPoP (Demonstration of Proof-of-Possession).
/// </summary>
public sealed class DPoPKeyPair : IDisposable
{
    private readonly ECDsa _key;
    private readonly string _publicKeyJwk;
    private readonly string _thumbprint;
    private bool _disposed;

    /// <summary>
    /// Gets the algorithm used by this key pair.
    /// </summary>
    public string Algorithm => "ES256";

    /// <summary>
    /// Gets the JWK thumbprint (base64url-encoded SHA-256 hash of the canonical JWK).
    /// </summary>
    public string Thumbprint => _thumbprint;

    /// <summary>
    /// Creates a new ES256 key pair.
    /// </summary>
    private DPoPKeyPair(ECDsa key, string publicKeyJwk, string thumbprint)
    {
        _key = key;
        _publicKeyJwk = publicKeyJwk;
        _thumbprint = thumbprint;
    }

    /// <summary>
    /// Generates a new ES256 key pair.
    /// </summary>
    public static DPoPKeyPair Generate()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: false);

        var x = Pkce.Base64UrlEncode(parameters.Q.X!);
        var y = Pkce.Base64UrlEncode(parameters.Q.Y!);

        // Build JWK for the public key (properties already in alphabetical order for thumbprint)
        var jwk = new EcJwk { Crv = "P-256", Kty = "EC", X = x, Y = y };
        var publicKeyJwk = JsonSerializer.Serialize(jwk, OAuthJsonContext.Default.EcJwk);

        // Compute JWK thumbprint (RFC 7638) - canonical form
        using var sha256 = SHA256.Create();
        var thumbprintBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(publicKeyJwk));
        var thumbprint = Pkce.Base64UrlEncode(thumbprintBytes);

        return new DPoPKeyPair(key, publicKeyJwk, thumbprint);
    }

    /// <summary>
    /// Creates a DPoP proof JWT.
    /// </summary>
    /// <param name="httpMethod">The HTTP method (e.g., "POST", "GET").</param>
    /// <param name="httpUri">The HTTP URI (without query string or fragment).</param>
    /// <param name="nonce">Optional server-provided nonce.</param>
    /// <param name="accessToken">Optional access token for access token hash (ath).</param>
    /// <returns>A signed DPoP proof JWT.</returns>
    public string CreateProof(string httpMethod, string httpUri, string? nonce = null, string? accessToken = null)
    {
        ThrowIfDisposed();

        // Build header
        var jwkObj = JsonSerializer.Deserialize(_publicKeyJwk, OAuthJsonContext.Default.EcJwk)!;
        var header = new DPoPProofHeader { Alg = "ES256", Typ = "dpop+jwt", Jwk = jwkObj };

        // Normalize URI (remove query and fragment)
        var uri = new Uri(httpUri);
        var normalizedUri = $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}";

        // Compute access token hash if needed
        string? ath = null;
        if (!string.IsNullOrEmpty(accessToken))
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(accessToken));
            ath = Pkce.Base64UrlEncode(hash);
        }

        // Build payload
        var payload = new DPoPProofPayload
        {
            Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Jti = Pkce.GenerateNonce(16),
            Htm = httpMethod.ToUpperInvariant(),
            Htu = normalizedUri,
            Nonce = string.IsNullOrEmpty(nonce) ? null : nonce,
            Ath = ath
        };

        return CreateDPoPJwt(header, payload);
    }

    /// <summary>
    /// Exports the public key as a JWK.
    /// </summary>
    public JsonWebKey ExportPublicKey()
    {
        ThrowIfDisposed();

        var parameters = _key.ExportParameters(includePrivateParameters: false);
        return new JsonWebKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = Pkce.Base64UrlEncode(parameters.Q.X!),
            Y = Pkce.Base64UrlEncode(parameters.Q.Y!),
            Alg = "ES256",
            Use = "sig"
        };
    }

    /// <summary>
    /// Exports the key pair (including private key) as a JWK.
    /// </summary>
    public JsonWebKey ExportKeyPair()
    {
        ThrowIfDisposed();

        var parameters = _key.ExportParameters(includePrivateParameters: true);
        return new JsonWebKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = Pkce.Base64UrlEncode(parameters.Q.X!),
            Y = Pkce.Base64UrlEncode(parameters.Q.Y!),
            D = Pkce.Base64UrlEncode(parameters.D!),
            Alg = "ES256",
            Use = "sig"
        };
    }

    /// <summary>
    /// Imports a key pair from a JWK.
    /// </summary>
    public static DPoPKeyPair Import(JsonWebKey jwk)
    {
        if (jwk.Kty != "EC" || jwk.Crv != "P-256")
        {
            throw new ArgumentException("Only EC keys with P-256 curve are supported.", nameof(jwk));
        }

        if (string.IsNullOrEmpty(jwk.X) || string.IsNullOrEmpty(jwk.Y) || string.IsNullOrEmpty(jwk.D))
        {
            throw new ArgumentException("JWK must contain x, y, and d components.", nameof(jwk));
        }

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

        var key = ECDsa.Create(parameters);

        // Build public JWK for thumbprint (properties in alphabetical order)
        var publicJwk = new EcJwk { Crv = "P-256", Kty = "EC", X = jwk.X!, Y = jwk.Y! };
        var publicKeyJwk = JsonSerializer.Serialize(publicJwk, OAuthJsonContext.Default.EcJwk);

        using var sha256 = SHA256.Create();
        var thumbprintBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(publicKeyJwk));
        var thumbprint = Pkce.Base64UrlEncode(thumbprintBytes);

        return new DPoPKeyPair(key, publicKeyJwk, thumbprint);
    }

    private string CreateDPoPJwt(DPoPProofHeader header, DPoPProofPayload payload)
    {
        var headerJson = JsonSerializer.Serialize(header, OAuthJsonContext.Default.DPoPProofHeader);
        var payloadJson = JsonSerializer.Serialize(payload, OAuthJsonContext.Default.DPoPProofPayload);

        var headerBase64 = Pkce.Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadBase64 = Pkce.Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var signingInput = $"{headerBase64}.{payloadBase64}";
        var signature = _key.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256);
        var signatureBase64 = Pkce.Base64UrlEncode(signature);

        return $"{signingInput}.{signatureBase64}";
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DPoPKeyPair));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _key.Dispose();
    }
}
