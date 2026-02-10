using System;
using System.Formats.Cbor;
using CarpaNet;
using CarpaNet.Cbor;
using Xunit;

namespace CarpaNet.UnitTests.Cbor;

public class DagCborReaderTests
{
    [Fact]
    public void ReadTextString_ReturnsString()
    {
        // Arrange: CBOR encoded "hello"
        var writer = new CborWriter();
        writer.WriteTextString("hello");
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var result = reader.ReadTextString();

        // Assert
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ReadInt32_ReturnsInteger()
    {
        // Arrange: CBOR encoded 42
        var writer = new CborWriter();
        writer.WriteInt32(42);
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var result = reader.ReadInt32();

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void ReadInt64_ReturnsLong()
    {
        // Arrange: CBOR encoded large number
        var writer = new CborWriter();
        writer.WriteInt64(9876543210L);
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var result = reader.ReadInt64();

        // Assert
        Assert.Equal(9876543210L, result);
    }

    [Fact]
    public void ReadBoolean_ReturnsTrue()
    {
        // Arrange: CBOR encoded true
        var writer = new CborWriter();
        writer.WriteBoolean(true);
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var result = reader.ReadBoolean();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ReadBoolean_ReturnsFalse()
    {
        // Arrange: CBOR encoded false
        var writer = new CborWriter();
        writer.WriteBoolean(false);
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var result = reader.ReadBoolean();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ReadByteString_ReturnsBytes()
    {
        // Arrange: CBOR encoded byte array
        var expected = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var writer = new CborWriter();
        writer.WriteByteString(expected);
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var result = reader.ReadByteString();

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReadStartMap_ReturnsCount()
    {
        // Arrange: CBOR map with 2 entries
        var writer = new CborWriter();
        writer.WriteStartMap(2);
        writer.WriteTextString("key1");
        writer.WriteTextString("value1");
        writer.WriteTextString("key2");
        writer.WriteInt32(123);
        writer.WriteEndMap();
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var count = reader.ReadStartMap();

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public void ReadStartArray_ReturnsCount()
    {
        // Arrange: CBOR array with 3 elements
        var writer = new CborWriter();
        writer.WriteStartArray(3);
        writer.WriteInt32(1);
        writer.WriteInt32(2);
        writer.WriteInt32(3);
        writer.WriteEndArray();
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var count = reader.ReadStartArray();

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void ReadCidLink_ReturnsCid()
    {
        // Arrange: Tag 42 + byte string with multibase prefix (0x00) + CID bytes
        // This simulates a CIDv1 with dag-cbor codec
        var cidBytes = new byte[] { 0x00, 0x01, 0x71, 0x12, 0x20, 0x01, 0x02, 0x03, 0x04 };
        var writer = new CborWriter();
        writer.WriteTag((CborTag)42);
        writer.WriteByteString(cidBytes);
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var cid = reader.ReadCidLink();

        // Assert
        Assert.NotNull(cid.Value);
        Assert.StartsWith("b", cid.Value); // base32 prefix
    }

    [Fact]
    public void ReadCidLink_ThrowsOnWrongTag()
    {
        // Arrange: Tag 1 (not 42)
        var writer = new CborWriter();
        writer.WriteTag(CborTag.UnixTimeSeconds);
        writer.WriteInt64(1234567890);
        var data = writer.Encode();

        // Act & Assert
        InvalidOperationException? exception = null;
        try
        {
            var reader = new DagCborReader(data);
            reader.ReadCidLink();
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }
        Assert.NotNull(exception);
    }

    [Fact]
    public void TryReadCidLink_ReturnsTrueForCid()
    {
        // Arrange: Tag 42 + byte string
        var cidBytes = new byte[] { 0x00, 0x01, 0x71, 0x12, 0x20, 0x01, 0x02, 0x03, 0x04 };
        var writer = new CborWriter();
        writer.WriteTag((CborTag)42);
        writer.WriteByteString(cidBytes);
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var result = reader.TryReadCidLink(out var cid);

        // Assert
        Assert.True(result);
        Assert.NotNull(cid.Value);
    }

    [Fact]
    public void TryReadCidLink_ReturnsFalseForNonTag()
    {
        // Arrange: Just a string
        var writer = new CborWriter();
        writer.WriteTextString("not a cid");
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var result = reader.TryReadCidLink(out var cid);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void PeekState_DoesNotAdvanceReader()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteTextString("hello");
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var state1 = reader.PeekState();
        var state2 = reader.PeekState();

        // Assert
        Assert.Equal(CborReaderState.TextString, state1);
        Assert.Equal(CborReaderState.TextString, state2);
        Assert.Equal(0, reader.BytesRead);
    }

    [Fact]
    public void BytesRead_TracksProgress()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteTextString("hello");
        var data = writer.Encode();

        // Act
        var reader = new DagCborReader(data);
        var before = reader.BytesRead;
        reader.ReadTextString();
        var after = reader.BytesRead;

        // Assert
        Assert.Equal(0, before);
        Assert.True(after > 0);
    }

    [Fact]
    public void AllowMultipleRootLevelValues_EnablesSequentialReads()
    {
        // Arrange: Two consecutive CBOR values
        var writer = new CborWriter();
        writer.WriteInt32(1);
        var data1 = writer.Encode();

        writer = new CborWriter();
        writer.WriteInt32(2);
        var data2 = writer.Encode();

        var combined = new byte[data1.Length + data2.Length];
        data1.CopyTo(combined, 0);
        data2.CopyTo(combined, data1.Length);

        // Act
        var reader = new DagCborReader(combined, allowMultipleRootLevelValues: true);
        var first = reader.ReadInt32();
        var second = reader.ReadInt32();

        // Assert
        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public void SkipValue_SkipsComplexValues()
    {
        // Arrange: Map with nested content followed by a number
        var writer = new CborWriter();
        writer.WriteStartMap(2);
        writer.WriteTextString("nested");
        writer.WriteStartArray(3);
        writer.WriteInt32(1);
        writer.WriteInt32(2);
        writer.WriteInt32(3);
        writer.WriteEndArray();
        writer.WriteTextString("key");
        writer.WriteTextString("value");
        writer.WriteEndMap();
        var mapData = writer.Encode();

        writer = new CborWriter();
        writer.WriteInt32(42);
        var numberData = writer.Encode();

        var combined = new byte[mapData.Length + numberData.Length];
        mapData.CopyTo(combined, 0);
        numberData.CopyTo(combined, mapData.Length);

        // Act
        var reader = new DagCborReader(combined, allowMultipleRootLevelValues: true);
        reader.SkipValue(); // Skip the map
        var number = reader.ReadInt32();

        // Assert
        Assert.Equal(42, number);
    }
}
