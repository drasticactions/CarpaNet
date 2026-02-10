using System;
using System.Formats.Cbor;

namespace CarpaNet.Cbor.Converters;

/// <summary>
/// CBOR converter for string values.
/// </summary>
public sealed class StringCborConverter : CborTypeConverter<string>
{
    /// <inheritdoc/>
    public override string? ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        return reader.ReadTextString();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, string? value)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteTextString(value);
        }
    }
}

/// <summary>
/// CBOR converter for 32-bit integer values.
/// </summary>
public sealed class Int32CborConverter : CborTypeConverter<int>
{
    /// <inheritdoc/>
    public override int ReadTyped(ref DagCborReader reader)
    {
        return reader.ReadInt32();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, int value)
    {
        writer.WriteInt32(value);
    }
}

/// <summary>
/// CBOR converter for 64-bit integer values.
/// </summary>
public sealed class Int64CborConverter : CborTypeConverter<long>
{
    /// <inheritdoc/>
    public override long ReadTyped(ref DagCborReader reader)
    {
        return reader.ReadInt64();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, long value)
    {
        writer.WriteInt64(value);
    }
}

/// <summary>
/// CBOR converter for unsigned 32-bit integer values.
/// </summary>
public sealed class UInt32CborConverter : CborTypeConverter<uint>
{
    /// <inheritdoc/>
    public override uint ReadTyped(ref DagCborReader reader)
    {
        return reader.ReadUInt32();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, uint value)
    {
        writer.WriteUInt32(value);
    }
}

/// <summary>
/// CBOR converter for unsigned 64-bit integer values.
/// </summary>
public sealed class UInt64CborConverter : CborTypeConverter<ulong>
{
    /// <inheritdoc/>
    public override ulong ReadTyped(ref DagCborReader reader)
    {
        return reader.ReadUInt64();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, ulong value)
    {
        writer.WriteUInt64(value);
    }
}

/// <summary>
/// CBOR converter for boolean values.
/// </summary>
public sealed class BooleanCborConverter : CborTypeConverter<bool>
{
    /// <inheritdoc/>
    public override bool ReadTyped(ref DagCborReader reader)
    {
        return reader.ReadBoolean();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, bool value)
    {
        writer.WriteBoolean(value);
    }
}

/// <summary>
/// CBOR converter for double values.
/// </summary>
public sealed class DoubleCborConverter : CborTypeConverter<double>
{
    /// <inheritdoc/>
    public override double ReadTyped(ref DagCborReader reader)
    {
        return reader.ReadDouble();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, double value)
    {
        writer.WriteDouble(value);
    }
}

/// <summary>
/// CBOR converter for float values.
/// </summary>
public sealed class SingleCborConverter : CborTypeConverter<float>
{
    /// <inheritdoc/>
    public override float ReadTyped(ref DagCborReader reader)
    {
        return reader.ReadSingle();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, float value)
    {
        writer.WriteSingle(value);
    }
}

/// <summary>
/// CBOR converter for byte array values.
/// </summary>
public sealed class ByteArrayCborConverter : CborTypeConverter<byte[]>
{
    /// <inheritdoc/>
    public override byte[]? ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        return reader.ReadByteString();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, byte[]? value)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteByteString(value);
        }
    }
}

/// <summary>
/// CBOR converter for DateTimeOffset values.
/// Writes as ISO 8601 string for maximum compatibility.
/// </summary>
public sealed class DateTimeOffsetCborConverter : CborTypeConverter<DateTimeOffset>
{
    /// <inheritdoc/>
    public override DateTimeOffset ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();

        // Check for tagged date/time (RFC 3339 or epoch)
        if (state == CborReaderState.Tag)
        {
            var tag = reader.ReadTag();

            // Tag 0: Standard date/time string (RFC 3339)
            if (tag == CborTag.DateTimeString)
            {
                var dateString = reader.ReadTextString();
                return DateTimeOffset.Parse(dateString);
            }

            // Tag 1: Epoch-based date/time
            if (tag == CborTag.UnixTimeSeconds)
            {
                state = reader.PeekState();
                if (state == CborReaderState.UnsignedInteger || state == CborReaderState.NegativeInteger)
                {
                    var seconds = reader.ReadInt64();
                    return DateTimeOffset.FromUnixTimeSeconds(seconds);
                }
                else if (state == CborReaderState.DoublePrecisionFloat || state == CborReaderState.SinglePrecisionFloat)
                {
                    var seconds = reader.ReadDouble();
                    return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
                }
            }

            throw new InvalidOperationException($"Unsupported date/time tag: {tag}");
        }

        // Untagged: try to parse as string or number
        if (state == CborReaderState.TextString)
        {
            var dateString = reader.ReadTextString();
            return DateTimeOffset.Parse(dateString);
        }

        if (state == CborReaderState.UnsignedInteger || state == CborReaderState.NegativeInteger)
        {
            var seconds = reader.ReadInt64();
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        if (state == CborReaderState.DoublePrecisionFloat || state == CborReaderState.SinglePrecisionFloat)
        {
            var seconds = reader.ReadDouble();
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
        }

        throw new InvalidOperationException($"Cannot read DateTimeOffset from CBOR state: {state}");
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, DateTimeOffset value)
    {
        // Write as ISO 8601 string for maximum compatibility
        writer.WriteTextString(value.ToString("O"));
    }
}

/// <summary>
/// CBOR converter for nullable DateTimeOffset values.
/// </summary>
public sealed class NullableDateTimeOffsetCborConverter : CborTypeConverter<DateTimeOffset?>
{
    private readonly DateTimeOffsetCborConverter _inner = new();

    /// <inheritdoc/>
    public override bool CanConvert(Type type)
    {
        return type == typeof(DateTimeOffset?);
    }

    /// <inheritdoc/>
    public override DateTimeOffset? ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        return _inner.ReadTyped(ref reader);
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, DateTimeOffset? value)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            _inner.WriteTyped(ref writer, value.Value);
        }
    }
}

/// <summary>
/// CBOR converter for nullable int values.
/// </summary>
public sealed class NullableInt32CborConverter : CborTypeConverter<int?>
{
    /// <inheritdoc/>
    public override bool CanConvert(Type type)
    {
        return type == typeof(int?);
    }

    /// <inheritdoc/>
    public override int? ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        return reader.ReadInt32();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, int? value)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteInt32(value.Value);
        }
    }
}

/// <summary>
/// CBOR converter for nullable long values.
/// </summary>
public sealed class NullableInt64CborConverter : CborTypeConverter<long?>
{
    /// <inheritdoc/>
    public override bool CanConvert(Type type)
    {
        return type == typeof(long?);
    }

    /// <inheritdoc/>
    public override long? ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        return reader.ReadInt64();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, long? value)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteInt64(value.Value);
        }
    }
}

/// <summary>
/// CBOR converter for nullable bool values.
/// </summary>
public sealed class NullableBooleanCborConverter : CborTypeConverter<bool?>
{
    /// <inheritdoc/>
    public override bool CanConvert(Type type)
    {
        return type == typeof(bool?);
    }

    /// <inheritdoc/>
    public override bool? ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        return reader.ReadBoolean();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, bool? value)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteBoolean(value.Value);
        }
    }
}

/// <summary>
/// CBOR converter for nullable double values.
/// </summary>
public sealed class NullableDoubleCborConverter : CborTypeConverter<double?>
{
    /// <inheritdoc/>
    public override bool CanConvert(Type type)
    {
        return type == typeof(double?);
    }

    /// <inheritdoc/>
    public override double? ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        return reader.ReadDouble();
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, double? value)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteDouble(value.Value);
        }
    }
}
