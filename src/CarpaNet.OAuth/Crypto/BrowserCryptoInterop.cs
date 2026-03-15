using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.OAuth.Crypto;

/// <summary>
/// JavaScript interop declarations for WebCrypto ECDSA P-256 operations.
/// </summary>
internal static partial class BrowserCryptoInterop
{
    private static Task? _initTask;

    /// <summary>
    /// Ensures the JavaScript module is loaded. Safe to call multiple times.
    /// </summary>
    internal static Task EnsureInitializedAsync()
    {
        return _initTask ??= InitializeCoreAsync();
    }

    private static async Task InitializeCoreAsync()
    {
        // Create a JS module from inline source using a data URI
        const string moduleSource = """
            export async function generateKeyPair() {
                const keyPair = await crypto.subtle.generateKey(
                    { name: "ECDSA", namedCurve: "P-256" },
                    true,
                    ["sign"]
                );
                const jwk = await crypto.subtle.exportKey("jwk", keyPair.privateKey);
                return JSON.stringify({ x: jwk.x, y: jwk.y, d: jwk.d });
            }

            export async function signData(x, y, d, dataBase64) {
                const jwk = { kty: "EC", crv: "P-256", x, y, d };
                const key = await crypto.subtle.importKey(
                    "jwk",
                    jwk,
                    { name: "ECDSA", namedCurve: "P-256" },
                    false,
                    ["sign"]
                );
                const data = Uint8Array.from(atob(dataBase64), c => c.charCodeAt(0));
                const sig = await crypto.subtle.sign(
                    { name: "ECDSA", hash: "SHA-256" },
                    key,
                    data
                );
                const bytes = new Uint8Array(sig);
                let binary = "";
                for (let i = 0; i < bytes.length; i++) {
                    binary += String.fromCharCode(bytes[i]);
                }
                return btoa(binary);
            }
            """;

        // Encode as data URI and import
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(moduleSource));
        var dataUri = $"data:text/javascript;base64,{base64}";

        await JSHost.ImportAsync("carpanet-crypto", dataUri).ConfigureAwait(false);
    }

    /// <summary>
    /// Generates a new ECDSA P-256 key pair and returns the JWK as JSON (with x, y, d fields).
    /// </summary>
    [JSImport("generateKeyPair", "carpanet-crypto")]
    internal static partial Task<string> GenerateKeyPairAsync();

    /// <summary>
    /// Signs data using ECDSA P-256 with SHA-256.
    /// Returns signature bytes as a base64-encoded string.
    /// </summary>
    [JSImport("signData", "carpanet-crypto")]
    internal static partial Task<string> SignDataRawAsync(
        string x,
        string y,
        string d,
        string dataBase64);

    /// <summary>
    /// Signs data and returns the signature as a byte array.
    /// </summary>
    internal static async Task<byte[]> SignDataAsync(string x, string y, string d, byte[] data)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var dataBase64 = Convert.ToBase64String(data);
        var resultBase64 = await SignDataRawAsync(x, y, d, dataBase64).ConfigureAwait(false);
        return Convert.FromBase64String(resultBase64);
    }
}
