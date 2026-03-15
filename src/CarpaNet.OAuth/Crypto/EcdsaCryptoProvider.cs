using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace CarpaNet.OAuth.Crypto;

/// <summary>
/// ECDsa-based crypto provider for desktop/server platforms.
/// </summary>
internal sealed class EcdsaCryptoProvider : ICryptoProvider
{
    private readonly ECDsa _key;

    private EcdsaCryptoProvider(ECDsa key)
    {
        _key = key;
    }

    /// <summary>
    /// Creates a new provider with a freshly generated P-256 key.
    /// </summary>
    public static EcdsaCryptoProvider Create()
    {
        return new EcdsaCryptoProvider(ECDsa.Create(ECCurve.NamedCurves.nistP256));
    }

    /// <summary>
    /// Creates a provider by importing JWK components.
    /// </summary>
    public static EcdsaCryptoProvider Import(string x, string y, string d)
    {
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Pkce.Base64UrlDecode(x),
                Y = Pkce.Base64UrlDecode(y)
            },
            D = Pkce.Base64UrlDecode(d)
        };

        return new EcdsaCryptoProvider(ECDsa.Create(parameters));
    }

    /// <inheritdoc/>
    public byte[] SignData(byte[] data)
    {
        return _key.SignData(data, HashAlgorithmName.SHA256);
    }

    /// <inheritdoc/>
    public Task<byte[]> SignDataAsync(byte[] data)
    {
        return Task.FromResult(SignData(data));
    }

    /// <inheritdoc/>
    public (string X, string Y) ExportPublicParameters()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        return (Pkce.Base64UrlEncode(parameters.Q.X!), Pkce.Base64UrlEncode(parameters.Q.Y!));
    }

    /// <inheritdoc/>
    public (string X, string Y, string D) ExportPrivateParameters()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: true);
        return (Pkce.Base64UrlEncode(parameters.Q.X!), Pkce.Base64UrlEncode(parameters.Q.Y!), Pkce.Base64UrlEncode(parameters.D!));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _key.Dispose();
    }
}
