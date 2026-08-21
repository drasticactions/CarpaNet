namespace CarpaNet.Jetstream;

/// <summary>
/// One row inside a sealed-segment block.
/// </summary>
public sealed class JetstreamSegmentRow
{
    /// <summary>Jetstream's sequence number for this event.</summary>
    public long Seq { get; set; }

    /// <summary>When Jetstream first saw the event, unix microseconds.</summary>
    public long WitnessedAt { get; set; }

    /// <summary>Operator-imported display timestamp, unix microseconds; 0 when none was imported.</summary>
    public long IndexedAt { get; set; }

    /// <summary>The row's event kind.</summary>
    public JetstreamSegmentRowKind Kind { get; set; }

    /// <summary>The repository DID.</summary>
    public string Did { get; set; } = string.Empty;

    /// <summary>The record's collection NSID; empty for non-commit kinds.</summary>
    public string Collection { get; set; } = string.Empty;

    /// <summary>The record key; empty for non-commit kinds.</summary>
    public string Rkey { get; set; } = string.Empty;

    /// <summary>The repo revision; empty for non-commit kinds.</summary>
    public string Rev { get; set; } = string.Empty;

    /// <summary>The raw DAG-CBOR payload bytes, or null when the row carries none (deletes).</summary>
    public byte[]? Payload { get; set; }

    /// <summary>
    /// The timestamp shown to subscribers on the wire: the operator-imported
    /// <see cref="IndexedAt"/> value when one was set, otherwise <see cref="WitnessedAt"/>.
    /// </summary>
    public long DisplayTimeUs => IndexedAt != 0 ? IndexedAt : WitnessedAt;
}
