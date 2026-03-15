using System;
using System.Threading.Tasks;

namespace CarpaNet.OAuth.Crypto;

/// <summary>
/// Abstracts platform-specific ECDSA P-256 operations for DPoP key pairs.
/// </summary>
internal interface ICryptoProvider : IDisposable
{
    /// <summary>
    /// Signs data synchronously using ES256. Throws <see cref="PlatformNotSupportedException"/> on browser.
    /// </summary>
    byte[] SignData(byte[] data);

    /// <summary>
    /// Signs data asynchronously using ES256. Works on all platforms.
    /// </summary>
    Task<byte[]> SignDataAsync(byte[] data);

    /// <summary>
    /// Exports the public key parameters as base64url-encoded strings.
    /// </summary>
    (string X, string Y) ExportPublicParameters();

    /// <summary>
    /// Exports the full key parameters (including private key) as base64url-encoded strings.
    /// </summary>
    (string X, string Y, string D) ExportPrivateParameters();
}
