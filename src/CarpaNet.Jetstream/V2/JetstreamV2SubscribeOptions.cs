using System;
using System.Collections.Generic;

namespace CarpaNet.Jetstream;

/// <summary>
/// Options for <see cref="JetstreamV2Client.SubscribeAsync"/>. The kind, DID, and collection
/// filters are independent predicates ANDed together; each is match-all when omitted.
/// </summary>
/// <remarks>
/// Stream modes, selected by the seq bounds:
/// <list type="bullet">
/// <item><description><b>Pure live</b> (default): no <see cref="AfterSeq"/>/<see cref="BeforeSeq"/>.
/// Tails the live websocket from <see cref="LiveCursor"/> (or the current tip when null).</description></item>
/// <item><description><b>Backfill then live</b>: set <see cref="AfterSeq"/> (0 replays the whole
/// archive). Sealed history is downloaded over HTTP, then the stream cuts over to the live
/// tail with no gap.</description></item>
/// <item><description><b>Snapshot only</b>: set <see cref="SnapshotOnly"/> with a seq bound.
/// Downloads and delivers the matched sealed range, then the stream ends without dialing
/// the websocket.</description></item>
/// </list>
/// </remarks>
public sealed class JetstreamV2SubscribeOptions
{
    internal const int MaxKinds = 4;
    internal const int MaxCollections = 100;
    internal const int MaxDids = 10000;

    /// <summary>
    /// Event kinds to receive. Null or empty means all kinds. Combine
    /// <see cref="JetstreamV2EventKind.Commit"/> with <see cref="Collections"/> for a
    /// commits-only collection stream.
    /// </summary>
    public IReadOnlyList<JetstreamV2EventKind>? Kinds { get; set; }

    /// <summary>
    /// Collection NSIDs or namespace wildcards ending in ".*" (e.g. "app.bsky.feed.*"),
    /// max 100 entries. Constrains commit events only: #identity, #account, and #sync events
    /// always bypass the collection filter (subject to <see cref="Dids"/>), because they are a
    /// folding consumer's only signal to purge a deleted account's records.
    /// </summary>
    public IReadOnlyList<string>? Collections { get; set; }

    /// <summary>
    /// Repo DIDs to receive events for, max 10,000 entries. Applies to every event kind.
    /// Null or empty means all repos.
    /// </summary>
    public IReadOnlyList<string>? Dids { get; set; }

    /// <summary>
    /// Exclusive lower sequence bound for an archive replay: only events with seq &gt; AfterSeq
    /// are delivered. Setting it (including 0 for the whole archive) starts with sealed-history
    /// replay before the client cuts over to the live tail. Null means no backfill.
    /// </summary>
    public long? AfterSeq { get; set; }

    /// <summary>
    /// Inclusive upper sequence bound for an archive snapshot: only events with
    /// seq &lt;= BeforeSeq are delivered. Requires <see cref="SnapshotOnly"/> — on a replay that
    /// continues into the live tail the same bound would silently drop every later live event.
    /// </summary>
    public long? BeforeSeq { get; set; }

    /// <summary>
    /// Turns a replay into a point-in-time archive snapshot: the matched sealed range is
    /// downloaded and delivered, then the stream ends without starting the live tail. Requires
    /// <see cref="AfterSeq"/> and/or <see cref="BeforeSeq"/>. Records in the active, unsealed
    /// segment (above the sealed tip) are only reachable via the live tail and are not included.
    /// </summary>
    public bool SnapshotOnly { get; set; }

    /// <summary>
    /// Resumes a pure live tail from a previously saved cursor (typically the last delivered
    /// <see cref="JetstreamV2Event.Seq"/>). The server replays inclusively and the client
    /// deduplicates the overlap. Null starts at the current live tip; 0 requests replay from
    /// the first retained event. Ignored when an archive replay is requested via
    /// <see cref="AfterSeq"/>/<see cref="BeforeSeq"/>, since that workflow computes its own
    /// live cutover cursor.
    /// </summary>
    public long? LiveCursor { get; set; }

    /// <summary>
    /// Asks the server to skip live events whose uncompressed frame (envelope included) exceeds
    /// this many bytes. Null or 0 means no limit.
    /// </summary>
    public long? MaxMessageSizeBytes { get; set; }

    /// <summary>
    /// Whether the caller asked for historical archive replay (any seq bound) versus a pure
    /// live tail.
    /// </summary>
    internal bool BackfillRequested => AfterSeq != null || BeforeSeq != null;

    /// <summary>
    /// Validates the option combination and filter limits, throwing <see cref="ArgumentException"/>
    /// on an invalid configuration.
    /// </summary>
    internal void Validate()
    {
        if (BeforeSeq != null && !SnapshotOnly)
        {
            throw new ArgumentException(
                $"{nameof(BeforeSeq)} requires {nameof(SnapshotOnly)}: on a replay that continues into the live tail, " +
                "an upper bound would silently drop every later live event.");
        }

        if (SnapshotOnly && AfterSeq == null && BeforeSeq == null)
        {
            throw new ArgumentException(
                $"{nameof(SnapshotOnly)} requires a replay bound ({nameof(AfterSeq)} and/or {nameof(BeforeSeq)}).");
        }

        if (AfterSeq is < 0)
        {
            throw new ArgumentException($"{nameof(AfterSeq)} must be non-negative.");
        }

        if (BeforeSeq is < 0)
        {
            throw new ArgumentException($"{nameof(BeforeSeq)} must be non-negative.");
        }

        if (LiveCursor is < 0)
        {
            throw new ArgumentException($"{nameof(LiveCursor)} must be non-negative.");
        }

        if (MaxMessageSizeBytes is < 0)
        {
            throw new ArgumentException($"{nameof(MaxMessageSizeBytes)} must be non-negative.");
        }

        if (Kinds != null && Kinds.Count > MaxKinds)
        {
            throw new ArgumentException($"{nameof(Kinds)} allows at most {MaxKinds} entries.");
        }

        if (Dids != null && Dids.Count > MaxDids)
        {
            throw new ArgumentException($"{nameof(Dids)} allows at most {MaxDids} entries.");
        }

        if (Collections != null)
        {
            if (Collections.Count > MaxCollections)
            {
                throw new ArgumentException($"{nameof(Collections)} allows at most {MaxCollections} entries.");
            }

            foreach (var collection in Collections)
            {
                if (string.IsNullOrEmpty(collection) || collection == ".*" || collection == "*")
                {
                    throw new ArgumentException(
                        $"{nameof(Collections)} entries must be exact NSIDs or namespace wildcards like \"app.bsky.feed.*\"; got \"{collection}\".");
                }
            }
        }
    }
}
