using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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

    [Fact]
    public void CreateProof_SignatureIsVerifiable()
    {
        // This test validates what a server does: extract the JWK from the header,
        // reconstruct the public key, and verify the ES256 signature.
        // If the browser WebCrypto implementation produces a different format,
        // this same verification would fail on the server.
        using var keyPair = DPoPKeyPair.Generate();

        var proof = keyPair.CreateProof("POST", "https://example.com/token", nonce: "test-nonce");

        AssertProofSignatureIsValid(proof);
    }

    [Fact]
    public void CreateProof_ImportedKey_SignatureIsVerifiable()
    {
        // Verify that export → import → sign produces verifiable signatures
        using var original = DPoPKeyPair.Generate();
        var jwk = original.ExportKeyPair();

        using var imported = DPoPKeyPair.Import(jwk);
        var proof = imported.CreateProof("POST", "https://example.com/token");

        AssertProofSignatureIsValid(proof);
    }

    [Fact]
    public void CreateProof_WithAccessToken_SignatureIsVerifiable()
    {
        using var keyPair = DPoPKeyPair.Generate();

        var proof = keyPair.CreateProof("GET", "https://example.com/api",
            nonce: "server-nonce", accessToken: "my-access-token");

        AssertProofSignatureIsValid(proof);
    }

    [Fact]
    public async Task CreateProofAsync_SignatureIsVerifiable()
    {
        using var keyPair = await DPoPKeyPair.GenerateAsync();

        var proof = await keyPair.CreateProofAsync("POST", "https://example.com/token", nonce: "test-nonce");

        AssertProofSignatureIsValid(proof);
    }

    [Fact]
    public async Task CreateProofAsync_MatchesSyncProofStructure()
    {
        // Verify that sync and async paths produce structurally identical JWTs
        // (different jti/iat values, but same header structure and signature format)
        using var keyPair = DPoPKeyPair.Generate();

        var syncProof = keyPair.CreateProof("POST", "https://example.com/token");
        var asyncProof = await keyPair.CreateProofAsync("POST", "https://example.com/token");

        // Both should be valid JWTs with verifiable signatures
        AssertProofSignatureIsValid(syncProof);
        AssertProofSignatureIsValid(asyncProof);

        // Both should have identical header structure
        var syncHeader = ParseJwtPart(syncProof.Split('.')[0]);
        var asyncHeader = ParseJwtPart(asyncProof.Split('.')[0]);

        Assert.Equal(
            syncHeader.RootElement.GetProperty("alg").GetString(),
            asyncHeader.RootElement.GetProperty("alg").GetString());
        Assert.Equal(
            syncHeader.RootElement.GetProperty("typ").GetString(),
            asyncHeader.RootElement.GetProperty("typ").GetString());

        // JWK in header should be identical (same key)
        Assert.Equal(
            syncHeader.RootElement.GetProperty("jwk").GetProperty("x").GetString(),
            asyncHeader.RootElement.GetProperty("jwk").GetProperty("x").GetString());
        Assert.Equal(
            syncHeader.RootElement.GetProperty("jwk").GetProperty("y").GetString(),
            asyncHeader.RootElement.GetProperty("jwk").GetProperty("y").GetString());

        syncHeader.Dispose();
        asyncHeader.Dispose();
    }

    [Fact]
    public void CreateProof_SignatureIs64Bytes()
    {
        // ES256 (ECDSA P-256) signatures in IEEE P1363 format are exactly 64 bytes
        // (32 bytes r + 32 bytes s). Both .NET ECDsa and WebCrypto produce this format.
        // If the signature were DER-encoded instead, it would be 70-72 bytes.
        using var keyPair = DPoPKeyPair.Generate();

        var proof = keyPair.CreateProof("POST", "https://example.com/token");

        var parts = proof.Split('.');
        var signatureBytes = Base64UrlDecode(parts[2]);

        Assert.Equal(64, signatureBytes.Length);
    }

    [Fact]
    public void CreateProof_JwkThumbprintMatchesHeader()
    {
        // The dpop_jkt claim that gets sent to the server must match the thumbprint
        // computed from the JWK in the proof header. This validates the thumbprint
        // computation is correct.
        using var keyPair = DPoPKeyPair.Generate();

        var proof = keyPair.CreateProof("POST", "https://example.com/token");

        var parts = proof.Split('.');
        var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
        using var headerDoc = JsonDocument.Parse(headerJson);

        var jwkElement = headerDoc.RootElement.GetProperty("jwk");
        var crv = jwkElement.GetProperty("crv").GetString()!;
        var kty = jwkElement.GetProperty("kty").GetString()!;
        var x = jwkElement.GetProperty("x").GetString()!;
        var y = jwkElement.GetProperty("y").GetString()!;

        // Compute thumbprint per RFC 7638: lexicographic JSON with required members
        var canonicalJwk = $"{{\"crv\":\"{crv}\",\"kty\":\"{kty}\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
        using var sha256 = SHA256.Create();
        var thumbprintBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonicalJwk));
        var computedThumbprint = Pkce.Base64UrlEncode(thumbprintBytes);

        Assert.Equal(computedThumbprint, keyPair.Thumbprint);
    }

    [Fact]
    public void CreateProof_AccessTokenHash_IsCorrectSha256()
    {
        // Verify the ath (access token hash) claim is computed correctly per RFC 9449
        using var keyPair = DPoPKeyPair.Generate();
        var accessToken = "test-access-token-12345";

        var proof = keyPair.CreateProof("GET", "https://example.com/api", accessToken: accessToken);

        var parts = proof.Split('.');
        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var payloadDoc = JsonDocument.Parse(payloadJson);

        var ath = payloadDoc.RootElement.GetProperty("ath").GetString()!;

        // Compute expected ath: base64url(SHA-256(ASCII(access_token)))
        using var sha256 = SHA256.Create();
        var expectedHash = sha256.ComputeHash(Encoding.ASCII.GetBytes(accessToken));
        var expectedAth = Pkce.Base64UrlEncode(expectedHash);

        Assert.Equal(expectedAth, ath);
    }

    [Fact]
    public void ClientAssertion_Create_ProducesVerifiableSignature()
    {
        using var keyPair = DPoPKeyPair.Generate();

        var jwt = ClientAssertion.Create("client-id", "https://auth.example.com", keyPair);

        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        // Verify signature using the public key
        var signingInput = $"{parts[0]}.{parts[1]}";
        var signatureBytes = Base64UrlDecode(parts[2]);

        var jwk = keyPair.ExportPublicKey();
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Pkce.Base64UrlDecode(jwk.X!),
                Y = Pkce.Base64UrlDecode(jwk.Y!)
            }
        });

        var isValid = ecdsa.VerifyData(
            Encoding.UTF8.GetBytes(signingInput),
            signatureBytes,
            HashAlgorithmName.SHA256);

        Assert.True(isValid, "Client assertion signature verification failed");
    }

    [Fact]
    public async Task ClientAssertion_CreateAsync_ProducesVerifiableSignature()
    {
        using var keyPair = await DPoPKeyPair.GenerateAsync();

        var jwt = await ClientAssertion.CreateAsync("client-id", "https://auth.example.com", keyPair);

        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        // Verify signature using the public key
        var signingInput = $"{parts[0]}.{parts[1]}";
        var signatureBytes = Base64UrlDecode(parts[2]);

        var jwk = keyPair.ExportPublicKey();
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Pkce.Base64UrlDecode(jwk.X!),
                Y = Pkce.Base64UrlDecode(jwk.Y!)
            }
        });

        var isValid = ecdsa.VerifyData(
            Encoding.UTF8.GetBytes(signingInput),
            signatureBytes,
            HashAlgorithmName.SHA256);

        Assert.True(isValid, "Client assertion async signature verification failed");
    }

    /// <summary>
    /// Extracts the JWK from a DPoP proof header, reconstructs the ECDSA public key,
    /// and verifies the signature — exactly what a server does to validate a DPoP proof.
    /// </summary>
    private static void AssertProofSignatureIsValid(string proof)
    {
        var parts = proof.Split('.');
        Assert.Equal(3, parts.Length);

        // Extract public key from the JWK in the header
        var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
        using var headerDoc = JsonDocument.Parse(headerJson);

        var jwkElement = headerDoc.RootElement.GetProperty("jwk");
        var x = Pkce.Base64UrlDecode(jwkElement.GetProperty("x").GetString()!);
        var y = Pkce.Base64UrlDecode(jwkElement.GetProperty("y").GetString()!);

        // Reconstruct the public key (as a server would)
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y }
        });

        // Verify the signature over the signing input (header.payload)
        var signingInput = $"{parts[0]}.{parts[1]}";
        var signature = Base64UrlDecode(parts[2]);

        var isValid = ecdsa.VerifyData(
            Encoding.UTF8.GetBytes(signingInput),
            signature,
            HashAlgorithmName.SHA256);

        Assert.True(isValid, "DPoP proof ES256 signature verification failed — " +
            "this would cause a 400 error from the authorization server");
    }

    private static JsonDocument ParseJwtPart(string base64UrlPart)
    {
        var json = Encoding.UTF8.GetString(Base64UrlDecode(base64UrlPart));
        return JsonDocument.Parse(json);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        return Pkce.Base64UrlDecode(input);
    }
}
