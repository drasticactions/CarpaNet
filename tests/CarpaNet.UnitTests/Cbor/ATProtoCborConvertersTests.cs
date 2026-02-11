using System;
using System.Formats.Cbor;
using CarpaNet;
using CarpaNet.Cbor;
using CarpaNet.Cbor.Converters;
using Xunit;

namespace CarpaNet.UnitTests.Cbor;

public class ATProtoCborConvertersTests
{
    [Fact]
    public void ATCidConverter_ReadsCidFromTag42()
    {
        // Arrange: Tag 42 + byte string with multibase prefix
        var cidBytes = new byte[] { 0x00, 0x01, 0x71, 0x12, 0x20, 0x01, 0x02, 0x03, 0x04 };
        var writer = new CborWriter();
        writer.WriteTag((CborTag)42);
        writer.WriteByteString(cidBytes);
        var data = writer.Encode();
        var converter = new ATCidCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.NotNull(result.Value);
        Assert.StartsWith("b", result.Value); // base32 prefix
    }

    [Fact]
    public void ATCidConverter_ReadsCidFromString()
    {
        // Arrange: Plain string CID
        var cidString = "bafyreihxk6cfeq6mnqxs4llekwjzpnzx";
        var writer = new CborWriter();
        writer.WriteTextString(cidString);
        var data = writer.Encode();
        var converter = new ATCidCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(cidString, result.Value);
    }

    [Fact]
    public void NullableATCidConverter_ReadsNull()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteNull();
        var data = writer.Encode();
        var converter = new NullableATCidCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ATDidConverter_ReadsDid()
    {
        // Arrange
        var did = "did:plc:z72i7hdynmk6r22z27h6tvur";
        var writer = new CborWriter();
        writer.WriteTextString(did);
        var data = writer.Encode();
        var converter = new ATDidCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(did, result.Value);
        Assert.True(ATDid.IsValid(result.Value));
        Assert.Equal("plc", result.Method);
    }

    [Fact]
    public void NullableATDidConverter_ReadsNull()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteNull();
        var data = writer.Encode();
        var converter = new NullableATDidCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ATHandleConverter_ReadsHandle()
    {
        // Arrange
        var handle = "alice.bsky.social";
        var writer = new CborWriter();
        writer.WriteTextString(handle);
        var data = writer.Encode();
        var converter = new ATHandleCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(handle, result.Value);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NullableATHandleConverter_ReadsNull()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteNull();
        var data = writer.Encode();
        var converter = new NullableATHandleCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ATUriConverter_ReadsUri()
    {
        // Arrange
        var uri = "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/3k2yihcrp6f2c";
        var writer = new CborWriter();
        writer.WriteTextString(uri);
        var data = writer.Encode();
        var converter = new ATUriCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(uri, result.Value);
        Assert.True(result.IsValid);
        Assert.Equal("did:plc:z72i7hdynmk6r22z27h6tvur", result.Authority);
        Assert.Equal("app.bsky.feed.post", result.Collection);
        Assert.Equal("3k2yihcrp6f2c", result.RecordKey);
    }

    [Fact]
    public void NullableATUriConverter_ReadsNull()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteNull();
        var data = writer.Encode();
        var converter = new NullableATUriCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ATIdentifierConverter_ReadsDid()
    {
        // Arrange
        var did = "did:plc:z72i7hdynmk6r22z27h6tvur";
        var writer = new CborWriter();
        writer.WriteTextString(did);
        var data = writer.Encode();
        var converter = new ATIdentifierCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(did, result.Value);
        Assert.True(result.IsDid);
        Assert.False(result.IsHandle);
    }

    [Fact]
    public void ATIdentifierConverter_ReadsHandle()
    {
        // Arrange
        var handle = "alice.bsky.social";
        var writer = new CborWriter();
        writer.WriteTextString(handle);
        var data = writer.Encode();
        var converter = new ATIdentifierCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(handle, result.Value);
        Assert.False(result.IsDid);
        Assert.True(result.IsHandle);
    }

    [Fact]
    public void ATBlobConverter_ReadsBlob()
    {
        // Arrange: Blob as CBOR map
        var writer = new CborWriter();
        writer.WriteStartMap(4);

        writer.WriteTextString("$type");
        writer.WriteTextString("blob");

        writer.WriteTextString("ref");
        writer.WriteTag((CborTag)42);
        var cidBytes = new byte[] { 0x00, 0x01, 0x71, 0x12, 0x20, 0x01, 0x02, 0x03, 0x04 };
        writer.WriteByteString(cidBytes);

        writer.WriteTextString("mimeType");
        writer.WriteTextString("image/jpeg");

        writer.WriteTextString("size");
        writer.WriteInt64(12345);

        writer.WriteEndMap();
        var data = writer.Encode();
        var converter = new ATBlobCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Ref.Value);
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.Equal(12345, result.Size);
    }

    [Fact]
    public void ATBlobConverter_ReadsNull()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteNull();
        var data = writer.Encode();
        var converter = new ATBlobCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ATBlobConverter_SkipsUnknownProperties()
    {
        // Arrange: Blob with extra unknown property
        var writer = new CborWriter();
        writer.WriteStartMap(5);

        writer.WriteTextString("$type");
        writer.WriteTextString("blob");

        writer.WriteTextString("unknownField");
        writer.WriteTextString("someValue");

        writer.WriteTextString("ref");
        writer.WriteTag((CborTag)42);
        var cidBytes = new byte[] { 0x00, 0x01, 0x71, 0x12, 0x20, 0x01, 0x02, 0x03, 0x04 };
        writer.WriteByteString(cidBytes);

        writer.WriteTextString("mimeType");
        writer.WriteTextString("text/plain");

        writer.WriteTextString("size");
        writer.WriteInt64(100);

        writer.WriteEndMap();
        var data = writer.Encode();
        var converter = new ATBlobCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("text/plain", result.MimeType);
        Assert.Equal(100, result.Size);
    }
}
