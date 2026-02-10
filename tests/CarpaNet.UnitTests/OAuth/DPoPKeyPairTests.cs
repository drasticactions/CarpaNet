using System;
using System.Text;
using System.Text.Json;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using Xunit;

namespace CarpaNet.UnitTests.OAuth;

public class DPoPKeyPairTests
{
    [Fact]
    public void Generate_CreatesValidKeyPair()
    {
        using var keyPair = DPoPKeyPair.Generate();

        Assert.Equal("ES256", keyPair.Algorithm);
        Assert.NotEmpty(keyPair.Thumbprint);
    }

    [Fact]
    public void Generate_ProducesUniqueKeys()
    {
        using var keyPair1 = DPoPKeyPair.Generate();
        using var keyPair2 = DPoPKeyPair.Generate();

        Assert.NotEqual(keyPair1.Thumbprint, keyPair2.Thumbprint);
    }

    [Fact]
    public void CreateProof_ProducesValidJwt()
    {
        using var keyPair = DPoPKeyPair.Generate();

        var proof = keyPair.CreateProof("POST", "https://example.com/token");

        // JWT has 3 parts separated by dots
        var parts = proof.Split('.');
        Assert.Equal(3, parts.Length);

        // Decode and verify header
        var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
        using var headerDoc = JsonDocument.Parse(headerJson);

        Assert.Equal("ES256", headerDoc.RootElement.GetProperty("alg").GetString());
        Assert.Equal("dpop+jwt", headerDoc.RootElement.GetProperty("typ").GetString());
        Assert.True(headerDoc.RootElement.TryGetProperty("jwk", out _));
    }

    [Fact]
    public void CreateProof_IncludesRequiredClaims()
    {
        using var keyPair = DPoPKeyPair.Generate();

        var proof = keyPair.CreateProof("GET", "https://example.com/api/data");

        var parts = proof.Split('.');
        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var payloadDoc = JsonDocument.Parse(payloadJson);

        Assert.True(payloadDoc.RootElement.TryGetProperty("iat", out _));
        Assert.True(payloadDoc.RootElement.TryGetProperty("jti", out _));
        Assert.Equal("GET", payloadDoc.RootElement.GetProperty("htm").GetString());
        Assert.Equal("https://example.com/api/data", payloadDoc.RootElement.GetProperty("htu").GetString());
    }

    [Fact]
    public void CreateProof_NormalizesUri()
    {
        using var keyPair = DPoPKeyPair.Generate();

        // URI with query string and fragment should be normalized
        var proof = keyPair.CreateProof("GET", "https://example.com/api/data?foo=bar#section");

        var parts = proof.Split('.');
        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var payloadDoc = JsonDocument.Parse(payloadJson);

        // Should not include query string or fragment
        Assert.Equal("https://example.com/api/data", payloadDoc.RootElement.GetProperty("htu").GetString());
    }

    [Fact]
    public void CreateProof_IncludesNonceWhenProvided()
    {
        using var keyPair = DPoPKeyPair.Generate();

        var proof = keyPair.CreateProof("POST", "https://example.com/token", nonce: "server-nonce");

        var parts = proof.Split('.');
        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var payloadDoc = JsonDocument.Parse(payloadJson);

        Assert.Equal("server-nonce", payloadDoc.RootElement.GetProperty("nonce").GetString());
    }

    [Fact]
    public void CreateProof_IncludesAccessTokenHash()
    {
        using var keyPair = DPoPKeyPair.Generate();

        var proof = keyPair.CreateProof("GET", "https://example.com/api", accessToken: "test-access-token");

        var parts = proof.Split('.');
        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var payloadDoc = JsonDocument.Parse(payloadJson);

        // ath (access token hash) should be present
        Assert.True(payloadDoc.RootElement.TryGetProperty("ath", out _));
    }

    [Fact]
    public void ExportPublicKey_ReturnsValidJwk()
    {
        using var keyPair = DPoPKeyPair.Generate();

        var jwk = keyPair.ExportPublicKey();

        Assert.Equal("EC", jwk.Kty);
        Assert.Equal("P-256", jwk.Crv);
        Assert.NotNull(jwk.X);
        Assert.NotNull(jwk.Y);
        Assert.Null(jwk.D); // Private key should not be included
    }

    [Fact]
    public void ExportKeyPair_IncludesPrivateKey()
    {
        using var keyPair = DPoPKeyPair.Generate();

        var jwk = keyPair.ExportKeyPair();

        Assert.Equal("EC", jwk.Kty);
        Assert.Equal("P-256", jwk.Crv);
        Assert.NotNull(jwk.X);
        Assert.NotNull(jwk.Y);
        Assert.NotNull(jwk.D); // Private key should be included
    }

    [Fact]
    public void Import_RestoresKeyPair()
    {
        using var original = DPoPKeyPair.Generate();
        var exportedJwk = original.ExportKeyPair();

        using var imported = DPoPKeyPair.Import(exportedJwk);

        Assert.Equal(original.Thumbprint, imported.Thumbprint);
    }

    [Fact]
    public void Import_CanSignWithRestoredKey()
    {
        using var original = DPoPKeyPair.Generate();
        var exportedJwk = original.ExportKeyPair();

        using var imported = DPoPKeyPair.Import(exportedJwk);

        // Should be able to create valid proofs
        var proof = imported.CreateProof("POST", "https://example.com/token");

        var parts = proof.Split('.');
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void Import_ThrowsForInvalidKeyType()
    {
        var invalidJwk = new JsonWebKey
        {
            Kty = "RSA", // Wrong key type
            Crv = "P-256"
        };

        Assert.Throws<ArgumentException>(() => DPoPKeyPair.Import(invalidJwk));
    }

    [Fact]
    public void Import_ThrowsForMissingComponents()
    {
        var incompleteJwk = new JsonWebKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = Pkce.Base64UrlEncode(new byte[32])
            // Missing Y and D
        };

        Assert.Throws<ArgumentException>(() => DPoPKeyPair.Import(incompleteJwk));
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var keyPair = DPoPKeyPair.Generate();

        keyPair.Dispose();
        keyPair.Dispose(); // Should not throw
    }

    [Fact]
    public void CreateProof_AfterDispose_Throws()
    {
        var keyPair = DPoPKeyPair.Generate();
        keyPair.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            keyPair.CreateProof("POST", "https://example.com/token"));
    }

    private static byte[] Base64UrlDecode(string input)
    {
        return Pkce.Base64UrlDecode(input);
    }
}
