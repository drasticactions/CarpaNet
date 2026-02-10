using System;
using System.Formats.Cbor;
using CarpaNet.Cbor;
using CarpaNet.Cbor.Converters;
using Xunit;

namespace CarpaNet.UnitTests.Cbor;

public class PrimitiveCborConvertersTests
{
    [Fact]
    public void StringConverter_ReadsString()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteTextString("test string");
        var data = writer.Encode();
        var converter = new StringCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal("test string", result);
    }

    [Fact]
    public void StringConverter_ReadsNull()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteNull();
        var data = writer.Encode();
        var converter = new StringCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Int32Converter_ReadsInteger()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteInt32(12345);
        var data = writer.Encode();
        var converter = new Int32CborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(12345, result);
    }

    [Fact]
    public void Int32Converter_ReadsNegativeInteger()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteInt32(-500);
        var data = writer.Encode();
        var converter = new Int32CborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(-500, result);
    }

    [Fact]
    public void Int64Converter_ReadsLong()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteInt64(9999999999L);
        var data = writer.Encode();
        var converter = new Int64CborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(9999999999L, result);
    }

    [Fact]
    public void BooleanConverter_ReadsTrue()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteBoolean(true);
        var data = writer.Encode();
        var converter = new BooleanCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void BooleanConverter_ReadsFalse()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteBoolean(false);
        var data = writer.Encode();
        var converter = new BooleanCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void DoubleConverter_ReadsDouble()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteDouble(3.14159);
        var data = writer.Encode();
        var converter = new DoubleCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(3.14159, result, precision: 5);
    }

    [Fact]
    public void SingleConverter_ReadsSingle()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteSingle(2.5f);
        var data = writer.Encode();
        var converter = new SingleCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(2.5f, result);
    }

    [Fact]
    public void ByteArrayConverter_ReadsBytes()
    {
        // Arrange
        var expected = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var writer = new CborWriter();
        writer.WriteByteString(expected);
        var data = writer.Encode();
        var converter = new ByteArrayCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ByteArrayConverter_ReadsNull()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteNull();
        var data = writer.Encode();
        var converter = new ByteArrayCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void DateTimeOffsetConverter_ReadsFromString()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteTextString("2024-01-15T10:30:00Z");
        var data = writer.Encode();
        var converter = new DateTimeOffsetCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(2024, result.Year);
        Assert.Equal(1, result.Month);
        Assert.Equal(15, result.Day);
        Assert.Equal(10, result.Hour);
        Assert.Equal(30, result.Minute);
    }

    [Fact]
    public void DateTimeOffsetConverter_ReadsFromEpoch()
    {
        // Arrange: Unix timestamp for 2024-01-15T00:00:00Z
        var epoch = 1705276800L;
        var writer = new CborWriter();
        writer.WriteInt64(epoch);
        var data = writer.Encode();
        var converter = new DateTimeOffsetCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(epoch), result);
    }

    [Fact]
    public void DateTimeOffsetConverter_ReadsFromTaggedEpoch()
    {
        // Arrange: Tagged Unix timestamp
        var epoch = 1705276800L;
        var writer = new CborWriter();
        writer.WriteTag(CborTag.UnixTimeSeconds);
        writer.WriteInt64(epoch);
        var data = writer.Encode();
        var converter = new DateTimeOffsetCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(epoch), result);
    }

    [Fact]
    public void NullableInt32Converter_ReadsValue()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteInt32(100);
        var data = writer.Encode();
        var converter = new NullableInt32CborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Equal(100, result);
    }

    [Fact]
    public void NullableInt32Converter_ReadsNull()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteNull();
        var data = writer.Encode();
        var converter = new NullableInt32CborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void NullableBooleanConverter_ReadsTrue()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteBoolean(true);
        var data = writer.Encode();
        var converter = new NullableBooleanCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void NullableBooleanConverter_ReadsNull()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteNull();
        var data = writer.Encode();
        var converter = new NullableBooleanCborConverter();

        // Act
        var reader = new DagCborReader(data);
        var result = converter.ReadTyped(ref reader);

        // Assert
        Assert.Null(result);
    }
}
