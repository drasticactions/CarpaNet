using System.Collections.Generic;

namespace CarpaNet.Jetstream;

/// <summary>
/// A request for <see cref="JetstreamV2Client.PlanSnapshotAsync"/>. Empty kind, DID, and
/// collection lists mean match-all. <see cref="AfterSeq"/> is an exclusive lower bound;
/// <see cref="BeforeSeq"/> (when set) is an inclusive upper bound.
/// </summary>
public sealed class JetstreamSnapshotPlanRequest
{
    /// <summary>Event kinds to include; null or empty includes all kinds.</summary>
    public IReadOnlyList<JetstreamV2EventKind>? Kinds { get; set; }

    /// <summary>Only include data for these DIDs; null or empty includes all DIDs.</summary>
    public IReadOnlyList<string>? Dids { get; set; }

    /// <summary>Collection NSIDs or namespace wildcards such as "app.bsky.feed.*"; constrains commit events only.</summary>
    public IReadOnlyList<string>? Collections { get; set; }

    /// <summary>Start after this sequence number (exclusive); 0 plans from the start of the archive.</summary>
    public long AfterSeq { get; set; }

    /// <summary>Stop at this sequence number (inclusive); null plans through the sealed tip.</summary>
    public long? BeforeSeq { get; set; }
}

/// <summary>
/// How a planned segment's rows should be fetched.
/// </summary>
public enum JetstreamSegmentPlanMode
{
    /// <summary>Download the whole segment file with getSegment.</summary>
    WholeSegment,

    /// <summary>Download only the listed block ranges with getBlock.</summary>
    Blocks,
}

/// <summary>
/// An inclusive range of block indices within a segment.
/// </summary>
public sealed class JetstreamBlockRange
{
    /// <summary>Index of the first block in the range.</summary>
    public int First { get; set; }

    /// <summary>Index of the last block in the range (inclusive).</summary>
    public int Last { get; set; }
}

/// <summary>
/// One unit of sealed-archive transport work: either a whole segment or a set of inclusive
/// block ranges within a segment. Seq bounds are transport hints with a one-sided contract
/// (no false negatives, possible false positives) — the client must re-apply exact filtering
/// after decode.
/// </summary>
public sealed class JetstreamPlannedSegment
{
    /// <summary>The segment filename accepted by getSegment and getBlock.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The zero-based segment index (ascending = creation order).</summary>
    public int Index { get; set; }

    /// <summary>The segment's xxh3 metadata checksum (16 hex chars). Equals the getSegment ETag
    /// and uniquely identifies a segment generation.</summary>
    public string Checksum { get; set; } = string.Empty;

    /// <summary>Lowest seq this entry may contain.</summary>
    public long MinSeq { get; set; }

    /// <summary>Highest seq this entry may contain.</summary>
    public long MaxSeq { get; set; }

    /// <summary>Whole-segment vs block-range download.</summary>
    public JetstreamSegmentPlanMode Mode { get; set; }

    /// <summary>The inclusive block ranges to fetch when <see cref="Mode"/> is
    /// <see cref="JetstreamSegmentPlanMode.Blocks"/>; empty otherwise.</summary>
    public IReadOnlyList<JetstreamBlockRange> Blocks { get; set; } = System.Array.Empty<JetstreamBlockRange>();
}

/// <summary>
/// The ordered transport plan returned by the server for a historical backfill query, plus the
/// sealed-archive coverage horizon.
/// </summary>
public sealed class JetstreamSnapshotPlan
{
    /// <summary>
    /// The continuation cursor: the highest sealed seq this page accounts for. Fetch the next
    /// page with afterSeq = PlannedThroughSeq; planning is complete once it reaches
    /// <see cref="SealedTipSeq"/>.
    /// </summary>
    public long PlannedThroughSeq { get; set; }

    /// <summary>
    /// The pagination goal: the sealed-archive tip, capped by beforeSeq when provided. Pin it
    /// from the first page (pass it as beforeSeq on later pages) so the snapshot does not move
    /// while it is being downloaded.
    /// </summary>
    public long SealedTipSeq { get; set; }

    /// <summary>The segments/block-ranges to download, in ascending order.</summary>
    public IReadOnlyList<JetstreamPlannedSegment> Segments { get; set; } = System.Array.Empty<JetstreamPlannedSegment>();
}
