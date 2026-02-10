using System;
using System.Security.Cryptography;
using System.Text;

namespace CarpaNet.OAuth.Crypto;

/// <summary>
/// PKCE (Proof Key for Code Exchange) implementation (RFC 7636).
/// </summary>
public static class Pkce
{
    /// <summary>
    /// The default verifier length in bytes (produces 43 character base64url string).
    /// </summary>
    public const int DefaultVerifierByteLength = 32;

    /// <summary>
    /// Generates a cryptographically random code verifier.
    /// </summary>
    /// <param name="byteLength">The number of random bytes to generate (default: 32, produces 43 chars).</param>
    /// <returns>A base64url-encoded code verifier.</returns>
    public static string GenerateVerifier(int byteLength = DefaultVerifierByteLength)
    {
        var bytes = new byte[byteLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Computes the S256 code challenge from a code verifier.
    /// </summary>
    /// <param name="verifier">The code verifier.</param>
    /// <returns>The base64url-encoded SHA-256 hash of the verifier.</returns>
    public static string ComputeChallenge(string verifier)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    /// <summary>
    /// Generates a PKCE code verifier and challenge pair.
    /// </summary>
    /// <returns>A tuple containing the verifier and its S256 challenge.</returns>
    public static (string Verifier, string Challenge) Generate()
    {
        var verifier = GenerateVerifier();
        var challenge = ComputeChallenge(verifier);
        return (verifier, challenge);
    }

    /// <summary>
    /// Generates a cryptographically random state parameter.
    /// </summary>
    /// <param name="byteLength">The number of random bytes to generate.</param>
    /// <returns>A base64url-encoded state value.</returns>
    public static string GenerateState(int byteLength = 16)
    {
        var bytes = new byte[byteLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Generates a cryptographically random nonce.
    /// </summary>
    /// <param name="byteLength">The number of random bytes to generate.</param>
    /// <returns>A base64url-encoded nonce value.</returns>
    public static string GenerateNonce(int byteLength = 16)
    {
        return GenerateState(byteLength);
    }

    /// <summary>
    /// Encodes bytes as base64url (RFC 4648).
    /// </summary>
    public static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Decodes a base64url string to bytes (RFC 4648).
    /// </summary>
    public static byte[] Base64UrlDecode(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');

        // Add padding if needed
        var remainder = base64.Length % 4;
        if (remainder == 2)
        {
            base64 += "==";
        }
        else if (remainder == 3)
        {
            base64 += "=";
        }

        return Convert.FromBase64String(base64);
    }
}
