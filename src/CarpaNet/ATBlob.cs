using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarpaNet;

/// <summary>
/// Represents a blob reference in ATProtocol.
/// Blobs are binary data stored separately from records.
/// In JSON format: { "$type": "blob", "ref": { "$link": "..." }, "mimeType": "...", "size": ... }
/// </summary>
[JsonConverter(typeof(ATBlobJsonConverter))]
public sealed class ATBlob
{
    /// <summary>
    /// The CID reference to the blob data.
    /// </summary>
    public ATCid Ref { get; set; }

    /// <summary>
    /// The MIME type of the blob.
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// The size of the blob in bytes.
    /// </summary>
    public long Size { get; set; }

    public ATBlob()
    {
        Ref = new ATCid(string.Empty);
    }

    public ATBlob(ATCid cid, string mimeType, long size)
    {
        Ref = cid;
        MimeType = mimeType;
        Size = size;
    }
}

public sealed class ATBlobJsonConverter : JsonConverter<ATBlob>
{
    public override ATBlob? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for blob");
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var blob = new ATBlob();

        if (root.TryGetProperty("ref", out var refProp))
        {
            if (refProp.TryGetProperty("$link", out var linkProp))
            {
                blob.Ref = new ATCid(linkProp.GetString() ?? string.Empty);
            }
        }

        if (root.TryGetProperty("mimeType", out var mimeTypeProp))
        {
            blob.MimeType = mimeTypeProp.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("size", out var sizeProp))
        {
            blob.Size = sizeProp.GetInt64();
        }

        return blob;
    }

    public override void Write(Utf8JsonWriter writer, ATBlob value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("$type", "blob");

        writer.WritePropertyName("ref");
        writer.WriteStartObject();
        writer.WriteString("$link", value.Ref.Value);
        writer.WriteEndObject();

        writer.WriteString("mimeType", value.MimeType);
        writer.WriteNumber("size", value.Size);
        writer.WriteEndObject();
    }
}
