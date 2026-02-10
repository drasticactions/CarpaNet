using System;
using System.Security.Cryptography;
using System.Text;
using CarpaNet.OAuth.Crypto;
using Xunit;

namespace CarpaNet.UnitTests.OAuth;

public class PkceTests
{
    [Fact]
    public void GenerateVerifier_ProducesCorrectLength()
    {
        var verifier = Pkce.GenerateVerifier();

        // 32 bytes -> 43 characters in base64url (without padding)
        Assert.Equal(43, verifier.Length);
    }

    [Fact]
    public void GenerateVerifier_IsBase64Url()
    {
        var verifier = Pkce.GenerateVerifier();

        // Should only contain base64url characters
        foreach (var c in verifier)
        {
            Assert.True(
                char.IsLetterOrDigit(c) || c == '-' || c == '_',
                $"Invalid character in verifier: {c}");
        }
    }

    [Fact]
    public void GenerateVerifier_IsCryptographicallyRandom()
    {
        var verifier1 = Pkce.GenerateVerifier();
        var verifier2 = Pkce.GenerateVerifier();

        Assert.NotEqual(verifier1, verifier2);
    }

    [Fact]
    public void ComputeChallenge_ProducesCorrectHash()
    {
        // Known test vector
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        var challenge = Pkce.ComputeChallenge(verifier);

        // Expected S256 challenge for this verifier
        var expected = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        Assert.Equal(expected, challenge);
    }

    [Fact]
    public void ComputeChallenge_ProducesCorrectLength()
    {
        var verifier = Pkce.GenerateVerifier();
        var challenge = Pkce.ComputeChallenge(verifier);

        // SHA-256 produces 32 bytes -> 43 characters in base64url
        Assert.Equal(43, challenge.Length);
    }

    [Fact]
    public void Generate_ProducesValidPair()
    {
        var (verifier, challenge) = Pkce.Generate();

        Assert.NotEmpty(verifier);
        Assert.NotEmpty(challenge);

        // Verify the challenge matches the verifier
        var computedChallenge = Pkce.ComputeChallenge(verifier);
        Assert.Equal(challenge, computedChallenge);
    }

    [Fact]
    public void GenerateState_ProducesRandomValue()
    {
        var state1 = Pkce.GenerateState();
        var state2 = Pkce.GenerateState();

        Assert.NotEqual(state1, state2);
    }

    [Fact]
    public void Base64UrlEncode_ProducesCorrectOutput()
    {
        var bytes = new byte[] { 0x00, 0x10, 0x83 };
        var encoded = Pkce.Base64UrlEncode(bytes);

        // Standard base64: ABCD
        // base64url removes padding and replaces + with - and / with _
        Assert.DoesNotContain("+", encoded);
        Assert.DoesNotContain("/", encoded);
        Assert.DoesNotContain("=", encoded);
    }

    [Fact]
    public void Base64UrlDecode_RoundTrips()
    {
        var original = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var encoded = Pkce.Base64UrlEncode(original);
        var decoded = Pkce.Base64UrlDecode(encoded);

        Assert.Equal(original, decoded);
    }
}
