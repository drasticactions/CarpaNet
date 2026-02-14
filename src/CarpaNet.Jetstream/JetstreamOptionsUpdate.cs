using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CarpaNet.Jetstream;

/// <summary>
/// A client-to-server message to dynamically update subscription filters.
/// </summary>
public sealed class JetstreamOptionsUpdate
{
    /// <summary>
    /// The message type. Must be "options_update".
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "options_update";

    /// <summary>
    /// The updated options payload.
    /// </summary>
    [JsonPropertyName("payload")]
    public JetstreamOptionsPayload Payload { get; set; } = new();
}

/// <summary>
/// Payload for an options_update message.
/// </summary>
public sealed class JetstreamOptionsPayload
{
    /// <summary>
    /// Collection NSIDs to filter on. Empty list disables the filter.
    /// </summary>
    [JsonPropertyName("wantedCollections")]
    public List<string>? WantedCollections { get; set; }

    /// <summary>
    /// Repo DIDs to filter on. Empty list disables the filter.
    /// </summary>
    [JsonPropertyName("wantedDids")]
    public List<string>? WantedDids { get; set; }

    /// <summary>
    /// Maximum event payload size in bytes.
    /// </summary>
    [JsonPropertyName("maxMessageSizeBytes")]
    public int? MaxMessageSizeBytes { get; set; }
}
