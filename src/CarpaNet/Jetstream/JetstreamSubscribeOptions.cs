using System.Collections.Generic;

namespace CarpaNet.Jetstream;

/// <summary>
/// Options for subscribing to a Jetstream event stream.
/// </summary>
public sealed class JetstreamSubscribeOptions
{
    /// <summary>
    /// Collection NSIDs to filter on. Supports prefix wildcards (e.g. "app.bsky.feed.*").
    /// Max 100 entries.
    /// </summary>
    public IReadOnlyList<string>? WantedCollections { get; set; }

    /// <summary>
    /// Repo DIDs to filter on. Max 10,000 entries.
    /// </summary>
    public IReadOnlyList<string>? WantedDids { get; set; }

    /// <summary>
    /// Unix microseconds timestamp to resume playback from.
    /// </summary>
    public long? Cursor { get; set; }

    /// <summary>
    /// Maximum event payload size in bytes. Events exceeding this are dropped. Zero or null means no limit.
    /// </summary>
    public int? MaxMessageSizeBytes { get; set; }

    /// <summary>
    /// Enable zstd compression. Requires a zstd decoder with the Jetstream custom dictionary.
    /// </summary>
    public bool Compress { get; set; }

    /// <summary>
    /// Pause event delivery until the client sends an options_update message.
    /// </summary>
    public bool RequireHello { get; set; }

    internal IEnumerable<KeyValuePair<string, string>> ToQueryParameters()
    {
        if (WantedCollections != null)
        {
            foreach (var collection in WantedCollections)
            {
                yield return new KeyValuePair<string, string>("wantedCollections", collection);
            }
        }

        if (WantedDids != null)
        {
            foreach (var did in WantedDids)
            {
                yield return new KeyValuePair<string, string>("wantedDids", did);
            }
        }

        if (Cursor.HasValue)
        {
            yield return new KeyValuePair<string, string>("cursor", Cursor.Value.ToString());
        }

        if (MaxMessageSizeBytes.HasValue)
        {
            yield return new KeyValuePair<string, string>("maxMessageSizeBytes", MaxMessageSizeBytes.Value.ToString());
        }

        if (Compress)
        {
            yield return new KeyValuePair<string, string>("compress", "true");
        }

        if (RequireHello)
        {
            yield return new KeyValuePair<string, string>("requireHello", "true");
        }
    }
}
