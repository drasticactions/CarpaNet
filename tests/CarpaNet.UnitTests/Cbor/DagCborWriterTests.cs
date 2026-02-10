using System;
using System.Formats.Cbor;
using CarpaNet;
using CarpaNet.Cbor;
using Xunit;

namespace CarpaNet.UnitTests.Cbor;

public class DagCborWriterTests
{
    [Fact]
    public void WriteTextString_ProducesValidCbor()
    {
        // Arrange & Act
        var writer = new DagCborWriter();
        writer.WriteTextString("hello");
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.Equal("hello", reader.ReadTextString());
    }

    [Fact]
    public void WriteInt32_ProducesValidCbor()
    {
        // Arrange & Act
        var writer = new DagCborWriter();
        writer.WriteInt32(42);
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.Equal(42, reader.ReadInt32());
    }

    [Fact]
    public void WriteInt64_ProducesValidCbor()
    {
        // Arrange & Act
        var writer = new DagCborWriter();
        writer.WriteInt64(9876543210L);
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.Equal(9876543210L, reader.ReadInt64());
    }

    [Fact]
    public void WriteBoolean_True_ProducesValidCbor()
    {
        // Arrange & Act
        var writer = new DagCborWriter();
        writer.WriteBoolean(true);
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.True(reader.ReadBoolean());
    }

    [Fact]
    public void WriteBoolean_False_ProducesValidCbor()
    {
        // Arrange & Act
        var writer = new DagCborWriter();
        writer.WriteBoolean(false);
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.False(reader.ReadBoolean());
    }

    [Fact]
    public void WriteNull_ProducesValidCbor()
    {
        // Arrange & Act
        var writer = new DagCborWriter();
        writer.WriteNull();
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        reader.ReadNull();
        Assert.Equal(CborReaderState.Finished, reader.PeekState());
    }

    [Fact]
    public void WriteDouble_ProducesValidCbor()
    {
        // Arrange & Act
        var writer = new DagCborWriter();
        writer.WriteDouble(3.14159);
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.Equal(3.14159, reader.ReadDouble());
    }

    [Fact]
    public void WriteByteString_ProducesValidCbor()
    {
        // Arrange
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        // Act
        var writer = new DagCborWriter();
        writer.WriteByteString(bytes);
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.Equal(bytes, reader.ReadByteString());
    }

    [Fact]
    public void WriteMap_ProducesValidCbor()
    {
        // Arrange & Act
        var writer = new DagCborWriter();
        writer.WriteStartMap(2);
        writer.WriteTextString("key1");
        writer.WriteTextString("value1");
        writer.WriteTextString("key2");
        writer.WriteInt32(123);
        writer.WriteEndMap();
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.Equal(2, reader.ReadStartMap());
        Assert.Equal("key1", reader.ReadTextString());
        Assert.Equal("value1", reader.ReadTextString());
        Assert.Equal("key2", reader.ReadTextString());
        Assert.Equal(123, reader.ReadInt32());
        reader.ReadEndMap();
    }

    [Fact]
    public void WriteArray_ProducesValidCbor()
    {
        // Arrange & Act
        var writer = new DagCborWriter();
        writer.WriteStartArray(3);
        writer.WriteInt32(1);
        writer.WriteInt32(2);
        writer.WriteInt32(3);
        writer.WriteEndArray();
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.Equal(3, reader.ReadStartArray());
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(3, reader.ReadInt32());
        reader.ReadEndArray();
    }

    [Fact]
    public void WriteCidLink_ProducesTag42()
    {
        // Arrange
        var cid = new ATCid("bafyreicqu7jhkc6ec3oq4fexqxlhkr27mjqcbaxkqz6aorpvvxwfkmmf3u");

        // Act
        var writer = new DagCborWriter();
        writer.WriteCidLink(cid);
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.Equal(CborReaderState.Tag, reader.PeekState());
        Assert.Equal((CborTag)42, reader.ReadTag());
        var cidBytes = reader.ReadByteString();
        // First byte should be multibase prefix 0x00
        Assert.Equal(0x00, cidBytes[0]);
    }

    [Fact]
    public void WriteCidLink_EmptyCid_WritesNull()
    {
        // Arrange
        var cid = new ATCid(string.Empty);

        // Act
        var writer = new DagCborWriter();
        writer.WriteCidLink(cid);
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.Equal(CborReaderState.Null, reader.PeekState());
    }

    [Fact]
    public void WriteCidLink_DefaultCid_WritesNull()
    {
        // Arrange - default ATCid has null Value
        var cid = default(ATCid);

        // Act
        var writer = new DagCborWriter();
        writer.WriteCidLink(cid);
        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.Equal(CborReaderState.Null, reader.PeekState());
    }

    [Fact]
    public void WriteComplexObject_ProducesValidCbor()
    {
        // Arrange & Act
        var writer = new DagCborWriter();
        writer.WriteStartMap(3);

        writer.WriteTextString("name");
        writer.WriteTextString("Test Object");

        writer.WriteTextString("nested");
        writer.WriteStartMap(2);
        writer.WriteTextString("value");
        writer.WriteInt32(42);
        writer.WriteTextString("active");
        writer.WriteBoolean(true);
        writer.WriteEndMap();

        writer.WriteTextString("items");
        writer.WriteStartArray(2);
        writer.WriteTextString("item1");
        writer.WriteTextString("item2");
        writer.WriteEndArray();

        writer.WriteEndMap();

        var data = writer.Encode();

        // Assert
        var reader = new CborReader(data);
        Assert.Equal(3, reader.ReadStartMap());

        Assert.Equal("name", reader.ReadTextString());
        Assert.Equal("Test Object", reader.ReadTextString());

        Assert.Equal("nested", reader.ReadTextString());
        Assert.Equal(2, reader.ReadStartMap());
        Assert.Equal("value", reader.ReadTextString());
        Assert.Equal(42, reader.ReadInt32());
        Assert.Equal("active", reader.ReadTextString());
        Assert.True(reader.ReadBoolean());
        reader.ReadEndMap();

        Assert.Equal("items", reader.ReadTextString());
        Assert.Equal(2, reader.ReadStartArray());
        Assert.Equal("item1", reader.ReadTextString());
        Assert.Equal("item2", reader.ReadTextString());
        reader.ReadEndArray();

        reader.ReadEndMap();
    }

    [Fact]
    public void BytesWritten_TracksProgress()
    {
        // Arrange
        var writer = new DagCborWriter();

        // Act
        var before = writer.BytesWritten;
        writer.WriteTextString("hello");
        var after = writer.BytesWritten;

        // Assert
        Assert.Equal(0, before);
        Assert.True(after > 0);
    }

    [Fact]
    public void RoundTrip_CidLink()
    {
        // Arrange - write a CID
        var originalCid = new ATCid("bafyreicqu7jhkc6ec3oq4fexqxlhkr27mjqcbaxkqz6aorpvvxwfkmmf3u");
        var writer = new DagCborWriter();
        writer.WriteCidLink(originalCid);
        var data = writer.Encode();

        // Act - read it back
        var dagReader = new DagCborReader(data);
        var readCid = dagReader.ReadCidLink();

        // Assert - CIDs should match
        Assert.Equal(originalCid.Value, readCid.Value);
    }
}
