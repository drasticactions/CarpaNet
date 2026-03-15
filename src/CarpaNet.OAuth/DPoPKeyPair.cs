using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CarpaNet.OAuth.Crypto;

/// <summary>
/// Represents an ES256 key pair for DPoP (Demonstration of Proof-of-Possession).
/// </summary>
public sealed class DPoPKeyPair : IDisposable
{
    private readonly ICryptoProvider _crypto;
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
    private DPoPKeyPair(ICryptoProvider crypto, string publicKeyJwk, string thumbprint)
    {
        _crypto = crypto;
        _publicKeyJwk = publicKeyJwk;
        _thumbprint = thumbprint;
    }

    /// <summary>
    /// Generates a new ES256 key pair.
    /// </summary>
    /// <remarks>
    /// On browser platforms, use <see cref="GenerateAsync"/> instead. This method throws
    /// <see cref="PlatformNotSupportedException"/> on browser.
    /// </remarks>
    public static DPoPKeyPair Generate()
    {
#if BROWSER
        throw new PlatformNotSupportedException(
            "Synchronous key generation is not supported on browser platforms. Use GenerateAsync instead.");
#else
        var crypto = EcdsaCryptoProvider.Create();
        return FromCryptoProvider(crypto);
#endif
    }

    /// <summary>
    /// Generates a new ES256 key pair asynchronously. Works on all platforms including browser.
    /// </summary>
    public static async Task<DPoPKeyPair> GenerateAsync()
    {
#if BROWSER
        var crypto = await BrowserCryptoProvider.CreateAsync().ConfigureAwait(false);
#else
        var crypto = EcdsaCryptoProvider.Create();
        await Task.CompletedTask.ConfigureAwait(false);
#endif
        return FromCryptoProvider(crypto);
    }

    /// <summary>
    /// Creates a DPoP proof JWT.
    /// </summary>
    /// <param name="httpMethod">The HTTP method (e.g., "POST", "GET").</param>
    /// <param name="httpUri">The HTTP URI (without query string or fragment).</param>
    /// <param name="nonce">Optional server-provided nonce.</param>
    /// <param name="accessToken">Optional access token for access token hash (ath).</param>
    /// <returns>A signed DPoP proof JWT.</returns>
    /// <remarks>
    /// On browser platforms, use <see cref="CreateProofAsync"/> instead. This method throws
    /// <see cref="PlatformNotSupportedException"/> on browser.
    /// </remarks>
    public string CreateProof(string httpMethod, string httpUri, string? nonce = null, string? accessToken = null)
    {
        ThrowIfDisposed();

        var (headerBase64, payloadBase64) = BuildProofComponents(httpMethod, httpUri, nonce, accessToken);
        var signingInput = $"{headerBase64}.{payloadBase64}";
        var signature = _crypto.SignData(Encoding.UTF8.GetBytes(signingInput));
        var signatureBase64 = Pkce.Base64UrlEncode(signature);

        return $"{signingInput}.{signatureBase64}";
    }

    /// <summary>
    /// Creates a DPoP proof JWT asynchronously. Works on all platforms including browser.
    /// </summary>
    /// <param name="httpMethod">The HTTP method (e.g., "POST", "GET").</param>
    /// <param name="httpUri">The HTTP URI (without query string or fragment).</param>
    /// <param name="nonce">Optional server-provided nonce.</param>
    /// <param name="accessToken">Optional access token for access token hash (ath).</param>
    /// <returns>A signed DPoP proof JWT.</returns>
    public async Task<string> CreateProofAsync(string httpMethod, string httpUri, string? nonce = null, string? accessToken = null)
    {
        ThrowIfDisposed();

        var (headerBase64, payloadBase64) = BuildProofComponents(httpMethod, httpUri, nonce, accessToken);
        var signingInput = $"{headerBase64}.{payloadBase64}";

        var signature = await _crypto.SignDataAsync(Encoding.UTF8.GetBytes(signingInput)).ConfigureAwait(false);
        var signatureBase64 = Pkce.Base64UrlEncode(signature);

        return $"{signingInput}.{signatureBase64}";
    }

    /// <summary>
    /// Signs data using the key pair. Throws <see cref="PlatformNotSupportedException"/> on browser.
    /// </summary>
    internal byte[] SignData(byte[] data)
    {
        ThrowIfDisposed();
        return _crypto.SignData(data);
    }

    /// <summary>
    /// Signs data asynchronously using the key pair. Works on all platforms.
    /// </summary>
    internal Task<byte[]> SignDataAsync(byte[] data)
    {
        ThrowIfDisposed();
        return _crypto.SignDataAsync(data);
    }

    /// <summary>
    /// Exports the public key as a JWK.
    /// </summary>
    public JsonWebKey ExportPublicKey()
    {
        ThrowIfDisposed();

        var (x, y) = _crypto.ExportPublicParameters();
        return new JsonWebKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = x,
            Y = y,
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

        var (x, y, d) = _crypto.ExportPrivateParameters();
        return new JsonWebKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = x,
            Y = y,
            D = d,
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

#if BROWSER
        ICryptoProvider crypto = BrowserCryptoProvider.Import(jwk.X!, jwk.Y!, jwk.D!);
#else
        ICryptoProvider crypto = EcdsaCryptoProvider.Import(jwk.X!, jwk.Y!, jwk.D!);
#endif

        return FromCryptoProvider(crypto);
    }

    private static DPoPKeyPair FromCryptoProvider(ICryptoProvider crypto)
    {
        var (x, y) = crypto.ExportPublicParameters();

        // Build JWK for the public key (properties already in alphabetical order for thumbprint)
        var jwk = new EcJwk { Crv = "P-256", Kty = "EC", X = x, Y = y };
        var publicKeyJwk = JsonSerializer.Serialize(jwk, OAuthJsonContext.Default.EcJwk);

        // Compute JWK thumbprint (RFC 7638) - canonical form
        using var sha256 = SHA256.Create();
        var thumbprintBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(publicKeyJwk));
        var thumbprint = Pkce.Base64UrlEncode(thumbprintBytes);

        return new DPoPKeyPair(crypto, publicKeyJwk, thumbprint);
    }

    private (string HeaderBase64, string PayloadBase64) BuildProofComponents(
        string httpMethod, string httpUri, string? nonce, string? accessToken)
    {
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

        var headerJson = JsonSerializer.Serialize(header, OAuthJsonContext.Default.DPoPProofHeader);
        var payloadJson = JsonSerializer.Serialize(payload, OAuthJsonContext.Default.DPoPProofPayload);

        var headerBase64 = Pkce.Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadBase64 = Pkce.Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        return (headerBase64, payloadBase64);
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
        _crypto.Dispose();
    }
}
