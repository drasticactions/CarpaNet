namespace CarpaNet.Jetstream;

/// <summary>
/// Discriminates which firehose event type a sealed-segment row represents.
/// Values are the on-disk wire format.
/// </summary>
public enum JetstreamSegmentRowKind : byte
{
    /// <summary>A record create.</summary>
    Create = 1,

    /// <summary>A record update.</summary>
    Update = 2,

    /// <summary>A record delete.</summary>
    Delete = 3,

    /// <summary>A #identity event; the payload is the upstream event's DAG-CBOR.</summary>
    Identity = 4,

    /// <summary>A #account event; the payload is the upstream event's DAG-CBOR.</summary>
    Account = 5,

    /// <summary>A #sync event; the payload is the upstream event's DAG-CBOR.</summary>
    Sync = 6,

    /// <summary>A resync replacement record (rendered as a create commit).</summary>
    CreateResync = 7,
}
