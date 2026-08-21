namespace CarpaNet.Jetstream;

/// <summary>
/// Discriminates the firehose event type carried by a <see cref="JetstreamV2Event"/>.
/// </summary>
public enum JetstreamV2EventKind
{
    /// <summary>A record create, update, or delete. <see cref="JetstreamV2Event.Commit"/> is non-null.</summary>
    Commit,

    /// <summary>A handle or DID document change. <see cref="JetstreamV2Event.Identity"/> is non-null.</summary>
    Identity,

    /// <summary>A hosting-status change. <see cref="JetstreamV2Event.Account"/> is non-null.</summary>
    Account,

    /// <summary>A repo divergence requiring resync. <see cref="JetstreamV2Event.Sync"/> is non-null.
    /// Sync events are delivered during archive replay and on the v2 live tail (never on the v1 wire).</summary>
    Sync,
}
