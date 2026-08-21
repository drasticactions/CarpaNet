namespace CarpaNet.Jetstream;

/// <summary>
/// A #sync event: the upstream signaled a repo divergence requiring a resync. The
/// authoritative replacement records follow as their own commit events.
/// </summary>
public sealed class JetstreamV2Sync
{
    /// <summary>
    /// The DID of the diverged repo.
    /// </summary>
    public string Did { get; set; } = string.Empty;

    /// <summary>
    /// The repo revision of the resync point.
    /// </summary>
    public string Rev { get; set; } = string.Empty;

    /// <summary>
    /// The upstream relay sequence number carried by the event (not Jetstream's seq).
    /// </summary>
    public long Seq { get; set; }

    /// <summary>
    /// The RFC 3339 timestamp from the upstream event.
    /// </summary>
    public string Time { get; set; } = string.Empty;
}
