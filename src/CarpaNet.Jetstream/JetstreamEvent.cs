using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarpaNet.Jetstream;

/// <summary>
/// A Jetstream event envelope containing a repo event with decoded record data.
/// </summary>
public sealed class JetstreamEvent
{
    /// <summary>
    /// The DID of the repo this event belongs to.
    /// </summary>
    [JsonPropertyName("did")]
    public string Did { get; set; } = string.Empty;

    /// <summary>
    /// Unix microseconds timestamp assigned by Jetstream.
    /// </summary>
    [JsonPropertyName("time_us")]
    public long TimeUs { get; set; }

    /// <summary>
    /// The kind of event: "commit", "identity", or "account".
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Commit event data. Present when <see cref="Kind"/> is "commit".
    /// </summary>
    [JsonPropertyName("commit")]
    public JetstreamCommit? Commit { get; set; }

    /// <summary>
    /// Identity event data. Present when <see cref="Kind"/> is "identity".
    /// </summary>
    [JsonPropertyName("identity")]
    public JetstreamIdentity? Identity { get; set; }

    /// <summary>
    /// Account event data. Present when <see cref="Kind"/> is "account".
    /// </summary>
    [JsonPropertyName("account")]
    public JetstreamAccount? Account { get; set; }
}

/// <summary>
/// A Jetstream commit event representing a single record operation.
/// </summary>
public sealed class JetstreamCommit
{
    /// <summary>
    /// The repo revision (TID).
    /// </summary>
    [JsonPropertyName("rev")]
    public string? Rev { get; set; }

    /// <summary>
    /// The operation type: "create", "update", or "delete".
    /// </summary>
    [JsonPropertyName("operation")]
    public string? Operation { get; set; }

    /// <summary>
    /// The collection NSID (e.g. "app.bsky.feed.post").
    /// </summary>
    [JsonPropertyName("collection")]
    public string? Collection { get; set; }

    /// <summary>
    /// The record key within the collection.
    /// </summary>
    [JsonPropertyName("rkey")]
    public string? Rkey { get; set; }

    /// <summary>
    /// The full record as a JSON element. Present on "create" and "update", absent on "delete".
    /// </summary>
    [JsonPropertyName("record")]
    public JsonElement? Record { get; set; }

    /// <summary>
    /// The CID of the record. Present on "create" and "update", absent on "delete".
    /// </summary>
    [JsonPropertyName("cid")]
    public string? Cid { get; set; }
}

/// <summary>
/// A Jetstream identity event indicating a DID document or handle change.
/// </summary>
public sealed class JetstreamIdentity
{
    /// <summary>
    /// The DID.
    /// </summary>
    [JsonPropertyName("did")]
    public string? Did { get; set; }

    /// <summary>
    /// The current handle.
    /// </summary>
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    /// <summary>
    /// The sequence number from the upstream firehose.
    /// </summary>
    [JsonPropertyName("seq")]
    public long? Seq { get; set; }

    /// <summary>
    /// Timestamp from the upstream firehose.
    /// </summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }
}

/// <summary>
/// A Jetstream account event indicating an account status change.
/// </summary>
public sealed class JetstreamAccount
{
    /// <summary>
    /// Whether the account is active.
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    /// <summary>
    /// The DID.
    /// </summary>
    [JsonPropertyName("did")]
    public string? Did { get; set; }

    /// <summary>
    /// The sequence number from the upstream firehose.
    /// </summary>
    [JsonPropertyName("seq")]
    public long? Seq { get; set; }

    /// <summary>
    /// Timestamp from the upstream firehose.
    /// </summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    /// <summary>
    /// Account status reason if not active (e.g. "deactivated", "takendown", "suspended", "deleted").
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
