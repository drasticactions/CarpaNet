using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CarpaNet.Jetstream;

/// <summary>
/// A single record mutation (create, update, or delete).
/// </summary>
public sealed class JetstreamV2Commit
{
    /// <summary>
    /// The operation: create, update, or delete.
    /// </summary>
    public JetstreamV2CommitOperation Operation { get; set; }

    /// <summary>
    /// The record's collection NSID, e.g. "app.bsky.feed.post".
    /// </summary>
    public string Collection { get; set; } = string.Empty;

    /// <summary>
    /// The record key within the collection.
    /// </summary>
    public string Rkey { get; set; } = string.Empty;

    /// <summary>
    /// The repo revision that produced this commit.
    /// </summary>
    public string Rev { get; set; } = string.Empty;

    /// <summary>
    /// The content identifier of the record. Null for deletes.
    /// </summary>
    public string? Cid { get; set; }

    /// <summary>
    /// The record in the atproto JSON data model (byte strings appear as
    /// <c>{"$bytes": ...}</c>, CID links as <c>{"$link": ...}</c>). Null for deletes.
    /// Use <see cref="GetRecord{T}(JsonTypeInfo{T})"/> to deserialize into a typed record.
    /// </summary>
    public JsonElement? Record { get; set; }

    /// <summary>
    /// Deserializes <see cref="Record"/> into a typed record using a source-generated
    /// <see cref="JsonTypeInfo{T}"/> (for example from a generated ATProtoJsonContext).
    /// Returns default when the commit carries no record (deletes).
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="typeInfo">The source-generated type info for <typeparamref name="T"/>.</param>
    /// <returns>The deserialized record, or default for deletes.</returns>
    public T? GetRecord<T>(JsonTypeInfo<T> typeInfo)
    {
        if (typeInfo == null)
        {
            throw new ArgumentNullException(nameof(typeInfo));
        }

        if (Record == null)
        {
            return default;
        }

        return Record.Value.Deserialize(typeInfo);
    }

    /// <summary>
    /// Attempts to deserialize <see cref="Record"/> into a typed record, returning false
    /// instead of throwing when the commit has no record or the record does not match the shape.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="typeInfo">The source-generated type info for <typeparamref name="T"/>.</param>
    /// <param name="record">The deserialized record on success.</param>
    /// <returns>True when a record was deserialized.</returns>
    public bool TryGetRecord<T>(JsonTypeInfo<T> typeInfo, out T? record)
    {
        record = default;
        if (typeInfo == null || Record == null)
        {
            return false;
        }

        try
        {
            record = Record.Value.Deserialize(typeInfo);
            return record != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
