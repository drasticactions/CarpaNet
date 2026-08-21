namespace CarpaNet.Jetstream;

/// <summary>
/// A #account event: a change to an account's hosting status, wrapping the upstream
/// com.atproto.sync.subscribeRepos event verbatim.
/// </summary>
public sealed class JetstreamV2Account
{
    /// <summary>
    /// The DID whose hosting status changed.
    /// </summary>
    public string Did { get; set; } = string.Empty;

    /// <summary>
    /// Whether the account is active on its host.
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// The inactive reason (e.g. "deleted", "suspended", "takendown") when <see cref="Active"/>
    /// is false; null when active.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// The upstream relay sequence number carried by the event (not Jetstream's seq).
    /// </summary>
    public long Seq { get; set; }

    /// <summary>
    /// The RFC 3339 timestamp from the upstream event.
    /// </summary>
    public string Time { get; set; } = string.Empty;
}
