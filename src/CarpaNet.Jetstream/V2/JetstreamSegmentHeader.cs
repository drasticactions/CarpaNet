namespace CarpaNet.Jetstream;

/// <summary>
/// The parsed fixed header at offset 0 of every sealed Jetstream segment file.
/// All offsets are absolute file offsets.
/// </summary>
public sealed class JetstreamSegmentHeader
{
    /// <summary>The segment format version (currently 1).</summary>
    public int Version { get; set; }

    /// <summary>Number of blocks in the segment.</summary>
    public int BlockCount { get; set; }

    /// <summary>Number of events across all blocks.</summary>
    public long EventCount { get; set; }

    /// <summary>Lowest sequence number stored in the segment.</summary>
    public long MinSeq { get; set; }

    /// <summary>Highest sequence number stored in the segment.</summary>
    public long MaxSeq { get; set; }

    /// <summary>Earliest witnessed-at time in the segment, unix microseconds.</summary>
    public long MinWitnessedAt { get; set; }

    /// <summary>Latest witnessed-at time in the segment, unix microseconds.</summary>
    public long MaxWitnessedAt { get; set; }

    /// <summary>Absolute file offset of the footer (which begins with the block index).</summary>
    public long FooterOffset { get; set; }

    /// <summary>Absolute file offset of the block index within the footer.</summary>
    public long BlockIndexOffset { get; set; }
}
