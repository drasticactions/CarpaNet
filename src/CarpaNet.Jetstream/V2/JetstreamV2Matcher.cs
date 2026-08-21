using System.Collections.Generic;

namespace CarpaNet.Jetstream;

/// <summary>
/// Applies the caller's exact kind/DID/collection/seq filters to archive rows and live events.
/// The snapshot planner is a one-sided transport hint (no false negatives, possible false
/// positives), so the client must re-apply exact filtering after decode; on the live tail the
/// same filters are forwarded to the server for pruning, but this matcher remains the
/// delivery authority.
/// </summary>
/// <remarks>
/// Presentation contract (matching the server's wire policy):
/// <list type="bullet">
/// <item><description>Kind and DID filters apply independently to all events.</description></item>
/// <item><description>With a collection filter set, only commit events whose collection matches are
/// delivered — but #account, #identity, and #sync always bypass the collection filter (subject to
/// the DID filter), because they are the consumer's only signal to purge a dead account's records.</description></item>
/// <item><description>The seq window is the exact (afterSeq, beforeSeq] bound, applied on top of the
/// planner's coarse pruning.</description></item>
/// </list>
/// </remarks>
internal sealed class JetstreamV2Matcher
{
    private readonly HashSet<JetstreamV2EventKind>? _kinds;
    private readonly HashSet<string>? _dids;
    private readonly HashSet<string>? _fullPaths;
    private readonly List<string>? _prefixes;
    private readonly long? _beforeSeq;
    private long _afterSeq;

    public JetstreamV2Matcher(JetstreamV2SubscribeOptions options)
    {
        _afterSeq = options.AfterSeq ?? 0;
        _beforeSeq = options.BeforeSeq;

        if (options.Kinds is { Count: > 0 })
        {
            _kinds = new HashSet<JetstreamV2EventKind>(options.Kinds);
        }

        if (options.Dids is { Count: > 0 })
        {
            _dids = new HashSet<string>(options.Dids);
        }

        if (options.Collections is { Count: > 0 })
        {
            foreach (var collection in options.Collections)
            {
                if (collection.EndsWith(".*", System.StringComparison.Ordinal))
                {
                    // Trim only the trailing "*", keeping the dot, so "app.bsky.feed.*"
                    // matches "app.bsky.feed."-prefixed NSIDs.
                    _prefixes ??= new List<string>();
                    _prefixes.Add(collection.Substring(0, collection.Length - 1));
                }
                else
                {
                    _fullPaths ??= new HashSet<string>();
                    _fullPaths.Add(collection);
                }
            }
        }
    }

    /// <summary>
    /// Reports whether a stored archive row passes the exact filters. Runs before the
    /// expensive record decode so filtered rows are never materialized.
    /// </summary>
    public bool WantsRow(JetstreamSegmentRow row) =>
        Wants(row.Seq, row.Did, PublicKind(row.Kind), row.Collection);

    /// <summary>
    /// Reports whether a decoded live event passes the exact filters.
    /// </summary>
    public bool WantsEvent(JetstreamV2Event evt)
    {
        var collection = evt.Kind == JetstreamV2EventKind.Commit ? evt.Commit?.Collection ?? string.Empty : string.Empty;
        return Wants(evt.Seq, evt.Did, evt.Kind, collection);
    }

    /// <summary>
    /// Raises the exclusive lower seq bound. Used on a re-backfill after a CursorTooOld
    /// rejection: the one plan unit that straddles the resume point is admitted whole under
    /// the planner's one-sided contract, and the raised floor drops its already-delivered rows
    /// before decode. The bound only ever moves forward.
    /// </summary>
    public void SetAfterSeq(long afterSeq) => _afterSeq = afterSeq;

    private bool Wants(long seq, string did, JetstreamV2EventKind? kind, string collection)
    {
        if (!WantsSeq(seq))
        {
            return false;
        }

        if (kind == null)
        {
            return false;
        }

        if (_kinds != null && !_kinds.Contains(kind.Value))
        {
            return false;
        }

        if (_dids != null && !_dids.Contains(did))
        {
            return false;
        }

        if (_fullPaths == null && _prefixes == null)
        {
            return true;
        }

        // DID-level events carry no collection and always bypass the collection filter,
        // subject to the DID filter applied above.
        if (kind != JetstreamV2EventKind.Commit)
        {
            return true;
        }

        // A commit lacking a collection bypasses the filter (wire parity).
        if (collection.Length == 0)
        {
            return true;
        }

        if (_fullPaths != null && _fullPaths.Contains(collection))
        {
            return true;
        }

        if (_prefixes != null)
        {
            foreach (var prefix in _prefixes)
            {
                if (collection.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool WantsSeq(long seq)
    {
        // afterSeq is a resume-after bound (seq > afterSeq), but only when one was actually
        // requested: 0 means "from the start of the archive" and seqs start at 1, so it
        // imposes no lower bound (matching the server).
        if (_afterSeq > 0 && seq <= _afterSeq)
        {
            return false;
        }

        if (_beforeSeq != null && seq > _beforeSeq.Value)
        {
            return false;
        }

        return true;
    }

    private static JetstreamV2EventKind? PublicKind(JetstreamSegmentRowKind kind)
    {
        switch (kind)
        {
            case JetstreamSegmentRowKind.Create:
            case JetstreamSegmentRowKind.Update:
            case JetstreamSegmentRowKind.Delete:
            case JetstreamSegmentRowKind.CreateResync:
                return JetstreamV2EventKind.Commit;
            case JetstreamSegmentRowKind.Identity:
                return JetstreamV2EventKind.Identity;
            case JetstreamSegmentRowKind.Account:
                return JetstreamV2EventKind.Account;
            case JetstreamSegmentRowKind.Sync:
                return JetstreamV2EventKind.Sync;
            default:
                return null;
        }
    }
}
