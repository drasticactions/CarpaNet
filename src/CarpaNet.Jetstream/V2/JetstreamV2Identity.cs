namespace CarpaNet.Jetstream;

/// <summary>
/// A #identity event: a change to an account's handle or DID document, wrapping the
/// upstream com.atproto.sync.subscribeRepos event verbatim.
/// </summary>
public sealed class JetstreamV2Identity
{
    /// <summary>
    /// The DID whose identity changed.
    /// </summary>
    public string Did { get; set; } = string.Empty;

    /// <summary>
    /// The account's new handle, or null when not present in the event.
    /// </summary>
    public string? Handle { get; set; }

    /// <summary>
    /// The upstream relay sequence number carried by the event (not Jetstream's seq).
    /// </summary>
    public long Seq { get; set; }

    /// <summary>
    /// The RFC 3339 timestamp from the upstream event.
    /// </summary>
    public string Time { get; set; } = string.Empty;
}
