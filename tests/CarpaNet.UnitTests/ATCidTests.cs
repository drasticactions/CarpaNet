using System;
using System.Text.Json;
using CarpaNet;
using Xunit;

namespace CarpaNet.UnitTests;

public class ATCidTests
{
    // Real CID examples from ATProtocol/Bluesky
    // These are CIDv1, dag-cbor (0x71), sha-256 (0x12), 32 bytes
    private const string ValidCidV1 = "bafyreicqu7jhkc6ec3oq4fexqxlhkr27mjqcbaxkqz6aorpvvxwfkmmf3u";
    private const string ValidCidV1Alt = "bafyreihxk6cfeqvjuxcz4jxhqmv6n4qwq7g4wc6f3p7h2j6qxqx5q6q5q";

    // CIDv0 example (base58btc, starts with Qm)
    private const string ValidCidV0 = "QmYwAPJzv5CZsnA625s3Xf2nemtYgPpHdWEz79ojWnPbdG";

    [Fact]
    public void Constructor_WithValidCidV1_ParsesCorrectly()
    {
        var cid = new ATCid(ValidCidV1);

        Assert.True(cid.IsValid);
        Assert.Equal(1, cid.Version);
        Assert.Equal(ATCid.MulticodecDagCbor, cid.Multicodec);
        Assert.Equal(ATCid.MultihashSha256, cid.MultihashType);
        Assert.Equal(ATCid.Sha256HashLength, cid.HashLength);
        Assert.NotNull(cid.Hash);
        Assert.Equal(32, cid.Hash!.Length);
    }

    [Fact]
    public void Constructor_WithValidCidV1_IsAtProtoBlessedFormat()
    {
        var cid = new ATCid(ValidCidV1);

        Assert.True(cid.IsAtProtoBlessedFormat);
        Assert.True(cid.IsCidV1);
        Assert.False(cid.IsCidV0);
    }

    [Fact]
    public void Constructor_WithValidCidV0_ParsesCorrectly()
    {
        var cid = new ATCid(ValidCidV0);

        Assert.True(cid.IsValid);
        Assert.Equal(0, cid.Version);
        Assert.Equal(ATCid.MulticodecDagPb, cid.Multicodec); // CIDv0 implicitly uses dag-pb
        Assert.Equal(ATCid.MultihashSha256, cid.MultihashType);
        Assert.Equal(ATCid.Sha256HashLength, cid.HashLength);
        Assert.NotNull(cid.Hash);
        Assert.Equal(32, cid.Hash!.Length);
    }

    [Fact]
    public void Constructor_WithValidCidV0_IsNotBlessedFormat()
    {
        var cid = new ATCid(ValidCidV0);

        Assert.False(cid.IsAtProtoBlessedFormat);
        Assert.True(cid.IsCidV0);
        Assert.False(cid.IsCidV1);
    }

    [Fact]
    public void Constructor_WithEmptyString_ReturnsInvalid()
    {
        var cid = new ATCid(string.Empty);

        Assert.False(cid.IsValid);
        Assert.Equal(-1, cid.Version);
        Assert.Null(cid.Hash);
    }

    [Fact]
    public void Constructor_WithNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ATCid(null!));
    }

    [Fact]
    public void Constructor_WithInvalidString_ReturnsInvalid()
    {
        var cid = new ATCid("invalid-cid-string");

        Assert.False(cid.IsValid);
        Assert.Equal(-1, cid.Version);
        Assert.Null(cid.Hash);
    }

    [Fact]
    public void FromSha256Hash_WithValidHash_CreatesBlessedFormatCid()
    {
        var hash = new byte[32];
        new Random(42).NextBytes(hash);

        var cid = ATCid.FromSha256Hash(hash);

        Assert.True(cid.IsValid);
        Assert.True(cid.IsAtProtoBlessedFormat);
        Assert.Equal(1, cid.Version);
        Assert.Equal(ATCid.MulticodecDagCbor, cid.Multicodec);
        Assert.Equal(ATCid.MultihashSha256, cid.MultihashType);
        Assert.Equal(32, cid.HashLength);
        Assert.Equal(hash, cid.Hash);
        Assert.StartsWith("b", cid.Value);
    }

    [Fact]
    public void FromSha256Hash_WithInvalidHashLength_ThrowsArgumentException()
    {
        var shortHash = new byte[16];

        Assert.Throws<ArgumentException>(() => ATCid.FromSha256Hash(shortHash));
    }

    [Fact]
    public void FromSha256Hash_WithNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ATCid.FromSha256Hash(null!));
    }

    [Fact]
    public void ToBytes_AndFromBytes_RoundTrips()
    {
        var cid = new ATCid(ValidCidV1);
        var bytes = cid.ToBytes();

        var reconstructed = ATCid.FromBytes(bytes);

        Assert.Equal(cid.Value, reconstructed.Value);
        Assert.Equal(cid.Version, reconstructed.Version);
        Assert.Equal(cid.Multicodec, reconstructed.Multicodec);
        Assert.Equal(cid.MultihashType, reconstructed.MultihashType);
        Assert.Equal(cid.HashLength, reconstructed.HashLength);
        Assert.Equal(cid.Hash, reconstructed.Hash);
    }

    [Fact]
    public void FromSha256Hash_AndToBytes_ProducesCorrectStructure()
    {
        var hash = new byte[32];
        for (int i = 0; i < 32; i++) hash[i] = (byte)i;

        var cid = ATCid.FromSha256Hash(hash);
        var bytes = cid.ToBytes();

        // CIDv1 structure: version (1) + multicodec (0x71) + multihash type (0x12) + hash length (0x20) + hash
        Assert.Equal(1 + 1 + 1 + 1 + 32, bytes.Length);
        Assert.Equal(1, bytes[0]); // Version 1
        Assert.Equal(0x71, bytes[1]); // dag-cbor
        Assert.Equal(0x12, bytes[2]); // sha-256
        Assert.Equal(0x20, bytes[3]); // 32 bytes

        // Verify hash bytes
        for (int i = 0; i < 32; i++)
        {
            Assert.Equal((byte)i, bytes[4 + i]);
        }
    }

    [Fact]
    public void Equality_SameValue_ReturnsTrue()
    {
        var cid1 = new ATCid(ValidCidV1);
        var cid2 = new ATCid(ValidCidV1);

        Assert.Equal(cid1, cid2);
        Assert.True(cid1 == cid2);
        Assert.False(cid1 != cid2);
        Assert.Equal(cid1.GetHashCode(), cid2.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_ReturnsFalse()
    {
        var cid1 = new ATCid(ValidCidV1);
        var cid2 = new ATCid(ValidCidV0);

        Assert.NotEqual(cid1, cid2);
        Assert.False(cid1 == cid2);
        Assert.True(cid1 != cid2);
    }

    [Fact]
    public void ImplicitConversion_ToString_ReturnsValue()
    {
        var cid = new ATCid(ValidCidV1);
        string value = cid;

        Assert.Equal(ValidCidV1, value);
    }

    [Fact]
    public void ImplicitConversion_FromString_CreatesCid()
    {
        ATCid cid = ValidCidV1;

        Assert.Equal(ValidCidV1, cid.Value);
        Assert.True(cid.IsValid);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var cid = new ATCid(ValidCidV1);

        Assert.Equal(ValidCidV1, cid.ToString());
    }

    [Fact]
    public void JsonSerialization_WritesLinkFormat()
    {
        var cid = new ATCid(ValidCidV1);

        var json = JsonSerializer.Serialize(cid);

        Assert.Contains("$link", json);
        Assert.Contains(ValidCidV1, json);
    }

    [Fact]
    public void JsonDeserialization_ReadsLinkFormat()
    {
        var json = $"{{\"$link\":\"{ValidCidV1}\"}}";

        var cid = JsonSerializer.Deserialize<ATCid>(json);

        Assert.Equal(ValidCidV1, cid.Value);
        Assert.True(cid.IsValid);
    }

    [Fact]
    public void JsonDeserialization_ReadsStringFormat()
    {
        var json = $"\"{ValidCidV1}\"";

        var cid = JsonSerializer.Deserialize<ATCid>(json);

        Assert.Equal(ValidCidV1, cid.Value);
        Assert.True(cid.IsValid);
    }

    [Theory]
    [InlineData("bafyreicqu7jhkc6ec3oq4fexqxlhkr27mjqcbaxkqz6aorpvvxwfkmmf3u", true)]  // Valid CIDv1
    [InlineData("bafkreihdwdcefgh4dqkjv67uzcmw7ojee6xedzdetojuzjevtenxquvyku", true)]  // Another valid CIDv1
    [InlineData("QmYwAPJzv5CZsnA625s3Xf2nemtYgPpHdWEz79ojWnPbdG", true)]  // Valid CIDv0
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData("b", false)]  // Just multibase prefix
    [InlineData("bafy", false)]  // Too short
    public void IsValid_ReturnsCorrectResult(string value, bool expected)
    {
        // Skip empty string as it requires special handling (can't call new ATCid(""))
        if (string.IsNullOrEmpty(value))
        {
            var cid = new ATCid(value ?? "invalid");
            Assert.False(cid.IsValid);
            return;
        }

        var cidInstance = new ATCid(value);
        Assert.Equal(expected, cidInstance.IsValid);
    }

    [Fact]
    public void DefaultValue_IsInvalid()
    {
        var cid = default(ATCid);

        // Default struct has null Value
        Assert.Null(cid.Value);
        Assert.False(cid.IsValid);
        Assert.False(cid.IsAtProtoBlessedFormat);
    }

    [Fact]
    public void MulticodecConstants_HaveCorrectValues()
    {
        Assert.Equal(0x71, ATCid.MulticodecDagCbor);
        Assert.Equal(0x70, ATCid.MulticodecDagPb);
        Assert.Equal(0x55, ATCid.MulticodecRaw);
    }

    [Fact]
    public void MultihashConstants_HaveCorrectValues()
    {
        Assert.Equal(0x12, ATCid.MultihashSha256);
        Assert.Equal(32, ATCid.Sha256HashLength);
    }

    [Fact]
    public void Hash_IsImmutable()
    {
        var hash = new byte[32];
        for (int i = 0; i < 32; i++) hash[i] = (byte)i;

        var cid = ATCid.FromSha256Hash(hash);

        // Modify original array
        hash[0] = 255;

        // CID hash should be unchanged (it's a copy)
        Assert.Equal(0, cid.Hash![0]);
    }

    [Fact]
    public void CidV1_WithRawMulticodec_ParsesCorrectly()
    {
        // Create a CIDv1 with raw multicodec (0x55) instead of dag-cbor
        // version (1) + multicodec (0x55) + multihash type (0x12) + hash length (0x20) + hash
        var cidBytes = new byte[1 + 1 + 1 + 1 + 32];
        cidBytes[0] = 1; // Version 1
        cidBytes[1] = 0x55; // raw multicodec
        cidBytes[2] = 0x12; // sha-256
        cidBytes[3] = 0x20; // 32 bytes

        var cid = ATCid.FromBytes(cidBytes);

        Assert.True(cid.IsValid);
        Assert.Equal(1, cid.Version);
        Assert.Equal(ATCid.MulticodecRaw, cid.Multicodec);
        Assert.Equal(ATCid.MultihashSha256, cid.MultihashType);
        Assert.False(cid.IsAtProtoBlessedFormat); // Not blessed because multicodec is not dag-cbor
    }
}
