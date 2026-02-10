using System;
using System.Formats.Cbor;

namespace CarpaNet.Cbor.Converters;

/// <summary>
/// CBOR converter for ATCid values (CID links with Tag 42).
/// </summary>
public sealed class ATCidCborConverter : CborTypeConverter<ATCid>
{
    /// <inheritdoc/>
    public override ATCid ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();

        // Null CID (e.g. for deletion ops where CID is not present)
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return default;
        }

        // CID can be represented as a Tag 42 link
        if (state == CborReaderState.Tag)
        {
            return reader.ReadCidLink();
        }

        // Or as a plain string (for backwards compatibility)
        if (state == CborReaderState.TextString)
        {
            var cidString = reader.ReadTextString();
            return new ATCid(cidString);
        }

        throw new InvalidOperationException($"Cannot read ATCid from CBOR state: {state}");
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, ATCid value)
    {
        if (value.Value == null || value.Value.Length == 0)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteCidLink(value);
        }
    }
}

/// <summary>
/// CBOR converter for nullable ATCid values.
/// </summary>
public sealed class NullableATCidCborConverter : CborTypeConverter<ATCid?>
{
    private readonly ATCidCborConverter _inner = new();

    /// <inheritdoc/>
    public override bool CanConvert(Type type)
    {
        return type == typeof(ATCid?);
    }

    /// <inheritdoc/>
    public override ATCid? ReadTyped(ref DagCborReader reader)
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
    public override void WriteTyped(ref DagCborWriter writer, ATCid? value)
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
/// CBOR converter for ATDid values.
/// </summary>
public sealed class ATDidCborConverter : CborTypeConverter<ATDid>
{
    /// <inheritdoc/>
    public override ATDid ReadTyped(ref DagCborReader reader)
    {
        var value = reader.ReadTextString();
        return new ATDid(value);
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, ATDid value)
    {
        if (value.Value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteTextString(value.Value);
        }
    }
}

/// <summary>
/// CBOR converter for nullable ATDid values.
/// </summary>
public sealed class NullableATDidCborConverter : CborTypeConverter<ATDid?>
{
    /// <inheritdoc/>
    public override bool CanConvert(Type type)
    {
        return type == typeof(ATDid?);
    }

    /// <inheritdoc/>
    public override ATDid? ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        var value = reader.ReadTextString();
        return new ATDid(value);
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, ATDid? value)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteTextString(value.Value.Value ?? string.Empty);
        }
    }
}

/// <summary>
/// CBOR converter for ATHandle values.
/// </summary>
public sealed class ATHandleCborConverter : CborTypeConverter<ATHandle>
{
    /// <inheritdoc/>
    public override ATHandle ReadTyped(ref DagCborReader reader)
    {
        var value = reader.ReadTextString();
        return new ATHandle(value);
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, ATHandle value)
    {
        if (value.Value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteTextString(value.Value);
        }
    }
}

/// <summary>
/// CBOR converter for nullable ATHandle values.
/// </summary>
public sealed class NullableATHandleCborConverter : CborTypeConverter<ATHandle?>
{
    /// <inheritdoc/>
    public override bool CanConvert(Type type)
    {
        return type == typeof(ATHandle?);
    }

    /// <inheritdoc/>
    public override ATHandle? ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        var value = reader.ReadTextString();
        return new ATHandle(value);
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, ATHandle? value)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteTextString(value.Value.Value ?? string.Empty);
        }
    }
}

/// <summary>
/// CBOR converter for ATUri values.
/// </summary>
public sealed class ATUriCborConverter : CborTypeConverter<ATUri>
{
    /// <inheritdoc/>
    public override ATUri ReadTyped(ref DagCborReader reader)
    {
        var value = reader.ReadTextString();
        return new ATUri(value);
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, ATUri value)
    {
        if (value.Value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteTextString(value.Value);
        }
    }
}

/// <summary>
/// CBOR converter for nullable ATUri values.
/// </summary>
public sealed class NullableATUriCborConverter : CborTypeConverter<ATUri?>
{
    /// <inheritdoc/>
    public override bool CanConvert(Type type)
    {
        return type == typeof(ATUri?);
    }

    /// <inheritdoc/>
    public override ATUri? ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        var value = reader.ReadTextString();
        return new ATUri(value);
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, ATUri? value)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteTextString(value.Value.Value ?? string.Empty);
        }
    }
}

/// <summary>
/// CBOR converter for ATIdentifier values.
/// </summary>
public sealed class ATIdentifierCborConverter : CborTypeConverter<ATIdentifier>
{
    /// <inheritdoc/>
    public override ATIdentifier ReadTyped(ref DagCborReader reader)
    {
        var value = reader.ReadTextString();
        return new ATIdentifier(value);
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, ATIdentifier value)
    {
        if (value.Value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteTextString(value.Value);
        }
    }
}

/// <summary>
/// CBOR converter for nullable ATIdentifier values.
/// </summary>
public sealed class NullableATIdentifierCborConverter : CborTypeConverter<ATIdentifier?>
{
    /// <inheritdoc/>
    public override bool CanConvert(Type type)
    {
        return type == typeof(ATIdentifier?);
    }

    /// <inheritdoc/>
    public override ATIdentifier? ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        var value = reader.ReadTextString();
        return new ATIdentifier(value);
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, ATIdentifier? value)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteTextString(value.Value.Value ?? string.Empty);
        }
    }
}

/// <summary>
/// CBOR converter for System.Text.Json.JsonElement values.
/// Used for "unknown" type properties where the schema is not known at compile time.
/// Converts CBOR to/from JsonElement by intermediate JSON representation.
/// </summary>
public sealed class JsonElementCborConverter : CborTypeConverter<System.Text.Json.JsonElement>
{
    /// <inheritdoc/>
    public override System.Text.Json.JsonElement ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return default;
        }

        // Read CBOR value and convert to JsonElement
        var jsonValue = ReadCborToJson(ref reader);
        return jsonValue;
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, System.Text.Json.JsonElement value)
    {
        WriteJsonToCbor(ref writer, value);
    }

    private static System.Text.Json.JsonElement ReadCborToJson(ref DagCborReader reader)
    {
        var state = reader.PeekState();

        return state switch
        {
            CborReaderState.Null => ReadNull(ref reader),
            CborReaderState.Boolean => ReadBoolean(ref reader),
            CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger => ReadInteger(ref reader),
            CborReaderState.SinglePrecisionFloat or CborReaderState.DoublePrecisionFloat or CborReaderState.HalfPrecisionFloat => ReadFloat(ref reader),
            CborReaderState.TextString => ReadString(ref reader),
            CborReaderState.ByteString => ReadByteString(ref reader),
            CborReaderState.StartArray => ReadArray(ref reader),
            CborReaderState.StartMap => ReadMap(ref reader),
            CborReaderState.Tag => ReadTagged(ref reader),
            _ => throw new InvalidOperationException($"Unsupported CBOR state for JsonElement conversion: {state}")
        };
    }

    private static System.Text.Json.JsonElement ReadNull(ref DagCborReader reader)
    {
        reader.ReadNull();
        return System.Text.Json.JsonDocument.Parse("null").RootElement.Clone();
    }

    private static System.Text.Json.JsonElement ReadBoolean(ref DagCborReader reader)
    {
        var value = reader.ReadBoolean();
        return System.Text.Json.JsonDocument.Parse(value ? "true" : "false").RootElement.Clone();
    }

    private static System.Text.Json.JsonElement ReadInteger(ref DagCborReader reader)
    {
        var value = reader.ReadInt64();
        return System.Text.Json.JsonDocument.Parse(value.ToString()).RootElement.Clone();
    }

    private static System.Text.Json.JsonElement ReadFloat(ref DagCborReader reader)
    {
        var value = reader.ReadDouble();
        return System.Text.Json.JsonDocument.Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone();
    }

    private static System.Text.Json.JsonElement ReadString(ref DagCborReader reader)
    {
        var value = reader.ReadTextString();
        return System.Text.Json.JsonDocument.Parse($"\"{EscapeJsonString(value)}\"").RootElement.Clone();
    }

    private static System.Text.Json.JsonElement ReadByteString(ref DagCborReader reader)
    {
        var bytes = reader.ReadByteString();
        var base64 = Convert.ToBase64String(bytes);
        return System.Text.Json.JsonDocument.Parse($"\"{base64}\"").RootElement.Clone();
    }

    private static System.Text.Json.JsonElement ReadArray(ref DagCborReader reader)
    {
        var elements = new System.Collections.Generic.List<System.Text.Json.JsonElement>();
        var count = reader.ReadStartArray();
        var remaining = count ?? int.MaxValue;

        while (remaining > 0 && reader.PeekState() != CborReaderState.EndArray)
        {
            elements.Add(ReadCborToJson(ref reader));
            remaining--;
        }

        reader.ReadEndArray();

        // Build JSON array
        using var stream = new System.IO.MemoryStream();
        using var jsonWriter = new System.Text.Json.Utf8JsonWriter(stream);
        jsonWriter.WriteStartArray();
        foreach (var elem in elements)
        {
            elem.WriteTo(jsonWriter);
        }
        jsonWriter.WriteEndArray();
        jsonWriter.Flush();

        stream.Position = 0;
        return System.Text.Json.JsonDocument.Parse(stream).RootElement.Clone();
    }

    private static System.Text.Json.JsonElement ReadMap(ref DagCborReader reader)
    {
        var properties = new System.Collections.Generic.List<(string Key, System.Text.Json.JsonElement Value)>();
        var count = reader.ReadStartMap();
        var remaining = count ?? int.MaxValue;

        while (remaining > 0 && reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();
            var value = ReadCborToJson(ref reader);
            properties.Add((key, value));
            remaining--;
        }

        reader.ReadEndMap();

        // Build JSON object
        using var stream = new System.IO.MemoryStream();
        using var jsonWriter = new System.Text.Json.Utf8JsonWriter(stream);
        jsonWriter.WriteStartObject();
        foreach (var (key, value) in properties)
        {
            jsonWriter.WritePropertyName(key);
            value.WriteTo(jsonWriter);
        }
        jsonWriter.WriteEndObject();
        jsonWriter.Flush();

        stream.Position = 0;
        return System.Text.Json.JsonDocument.Parse(stream).RootElement.Clone();
    }

    private static System.Text.Json.JsonElement ReadTagged(ref DagCborReader reader)
    {
        var tag = reader.ReadTag();

        // Handle CID links (Tag 42)
        if (tag == DagCborReader.CidTag)
        {
            var bytes = reader.ReadByteString();
            // Skip multibase prefix if present
            var offset = bytes.Length > 0 && bytes[0] == 0x00 ? 1 : 0;
            var cidData = new byte[bytes.Length - offset];
            Array.Copy(bytes, offset, cidData, 0, cidData.Length);
            var cidString = "b" + Base32Encode(cidData);
            return System.Text.Json.JsonDocument.Parse($"\"cid:{cidString}\"").RootElement.Clone();
        }

        // For other tags, just read the value
        return ReadCborToJson(ref reader);
    }

    private static void WriteJsonToCbor(ref DagCborWriter writer, System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Null:
            case System.Text.Json.JsonValueKind.Undefined:
                writer.WriteNull();
                break;

            case System.Text.Json.JsonValueKind.True:
                writer.WriteBoolean(true);
                break;

            case System.Text.Json.JsonValueKind.False:
                writer.WriteBoolean(false);
                break;

            case System.Text.Json.JsonValueKind.Number:
                if (element.TryGetInt64(out var intValue))
                {
                    writer.WriteInt64(intValue);
                }
                else
                {
                    writer.WriteDouble(element.GetDouble());
                }
                break;

            case System.Text.Json.JsonValueKind.String:
                writer.WriteTextString(element.GetString() ?? string.Empty);
                break;

            case System.Text.Json.JsonValueKind.Array:
                writer.WriteStartArray(element.GetArrayLength());
                foreach (var item in element.EnumerateArray())
                {
                    WriteJsonToCbor(ref writer, item);
                }
                writer.WriteEndArray();
                break;

            case System.Text.Json.JsonValueKind.Object:
                var propCount = 0;
                foreach (var _ in element.EnumerateObject()) propCount++;

                writer.WriteStartMap(propCount);
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WriteTextString(prop.Name);
                    WriteJsonToCbor(ref writer, prop.Value);
                }
                writer.WriteEndMap();
                break;
        }
    }

    private static string EscapeJsonString(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

    private static string Base32Encode(byte[] data)
    {
        if (data.Length == 0) return string.Empty;

        const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var result = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);

        int buffer = 0;
        int bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                var index = (buffer >> bitsLeft) & 0x1F;
                result.Append(Alphabet[index]);
            }
        }

        if (bitsLeft > 0)
        {
            var index = (buffer << (5 - bitsLeft)) & 0x1F;
            result.Append(Alphabet[index]);
        }

        return result.ToString();
    }
}

/// <summary>
/// CBOR converter for ATBlob values.
/// ATBlob is represented as a map with $type, ref (CID link), mimeType, and size.
/// </summary>
public sealed class ATBlobCborConverter : CborTypeConverter<ATBlob>
{
    private readonly ATCidCborConverter _cidConverter = new();

    /// <inheritdoc/>
    public override ATBlob? ReadTyped(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        if (state != CborReaderState.StartMap)
        {
            throw new InvalidOperationException($"Expected map for ATBlob, got {state}");
        }

        var count = reader.ReadStartMap();
        var blob = new ATBlob();

        var remaining = count ?? int.MaxValue;
        while (remaining > 0 && reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();

            switch (key)
            {
                case "$type":
                    // Skip the type discriminator
                    reader.ReadTextString();
                    break;

                case "ref":
                    // ref is a CID link (Tag 42)
                    blob.Ref = reader.ReadCidLink();
                    break;

                case "mimeType":
                    blob.MimeType = reader.ReadTextString();
                    break;

                case "size":
                    blob.Size = reader.ReadInt64();
                    break;

                default:
                    // Skip unknown properties
                    reader.SkipValue();
                    break;
            }

            remaining--;
        }

        reader.ReadEndMap();
        return blob;
    }

    /// <inheritdoc/>
    public override void WriteTyped(ref DagCborWriter writer, ATBlob? value)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        // Count non-null properties
        var propertyCount = 1; // $type is always written
        if (value.Ref.Value != null) propertyCount++;
        if (value.MimeType != null) propertyCount++;
        propertyCount++; // size is always written

        writer.WriteStartMap(propertyCount);

        // Write $type
        writer.WriteTextString("$type");
        writer.WriteTextString("blob");

        // Write ref
        if (value.Ref.Value != null)
        {
            writer.WriteTextString("ref");
            _cidConverter.WriteTyped(ref writer, value.Ref);
        }

        // Write mimeType
        if (value.MimeType != null)
        {
            writer.WriteTextString("mimeType");
            writer.WriteTextString(value.MimeType);
        }

        // Write size
        writer.WriteTextString("size");
        writer.WriteInt64(value.Size);

        writer.WriteEndMap();
    }
}
