using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace CarpaNet.OAuth.Crypto;

/// <summary>
/// WebCrypto-based crypto provider for browser (WASM) platforms.
/// Uses JavaScript interop via <see cref="BrowserCryptoInterop"/>.
/// </summary>
internal sealed class BrowserCryptoProvider : ICryptoProvider
{
    private readonly string _x;
    private readonly string _y;
    private readonly string _d;

    private BrowserCryptoProvider(string x, string y, string d)
    {
        _x = x;
        _y = y;
        _d = d;
    }

    /// <summary>
    /// Creates a new provider by generating a P-256 key pair via WebCrypto.
    /// </summary>
    public static async Task<BrowserCryptoProvider> CreateAsync()
    {
        await BrowserCryptoInterop.EnsureInitializedAsync().ConfigureAwait(false);
        var json = await BrowserCryptoInterop.GenerateKeyPairAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var x = root.GetProperty("x").GetString()
            ?? throw new InvalidOperationException("Failed to generate key pair via WebCrypto: missing 'x'.");
        var y = root.GetProperty("y").GetString()
            ?? throw new InvalidOperationException("Failed to generate key pair via WebCrypto: missing 'y'.");
        var d = root.GetProperty("d").GetString()
            ?? throw new InvalidOperationException("Failed to generate key pair via WebCrypto: missing 'd'.");
        return new BrowserCryptoProvider(x, y, d);
    }

    /// <summary>
    /// Creates a provider by importing JWK components (sync, no validation).
    /// </summary>
    public static BrowserCryptoProvider Import(string x, string y, string d)
    {
        return new BrowserCryptoProvider(x, y, d);
    }

    /// <inheritdoc/>
    public byte[] SignData(byte[] data)
    {
        throw new PlatformNotSupportedException(
            "Synchronous ECDSA signing is not supported on browser platforms. Use SignDataAsync instead.");
    }

    /// <inheritdoc/>
    public async Task<byte[]> SignDataAsync(byte[] data)
    {
        return await BrowserCryptoInterop.SignDataAsync(_x, _y, _d, data).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public (string X, string Y) ExportPublicParameters()
    {
        return (_x, _y);
    }

    /// <inheritdoc/>
    public (string X, string Y, string D) ExportPrivateParameters()
    {
        return (_x, _y, _d);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // No-op: no unmanaged resources
    }
}
