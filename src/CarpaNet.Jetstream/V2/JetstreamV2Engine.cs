using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CarpaNet.Jetstream;

/// <summary>
/// Orchestrates the managed Jetstream v2 stream.
/// </summary>
internal sealed class JetstreamV2Engine
{
    private const int MaxRebackfillStalls = 5;

    private readonly Uri _baseUri;
    private readonly JetstreamV2ClientOptions _clientOptions;
    private readonly JetstreamV2Transport _transport;
    private readonly ILogger _logger;

    public JetstreamV2Engine(Uri baseUri, JetstreamV2ClientOptions clientOptions, JetstreamV2Transport transport, ILogger logger)
    {
        _baseUri = baseUri;
        _clientOptions = clientOptions;
        _transport = transport;
        _logger = logger;
    }

    public async IAsyncEnumerable<JetstreamV2Event> RunAsync(
        JetstreamV2SubscribeOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var matcher = new JetstreamV2Matcher(options);

        if (!options.BackfillRequested)
        {
            await foreach (var evt in TailLiveAsync(options, matcher, options.LiveCursor, options.LiveCursor ?? 0, cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }

            yield break;
        }

        var cursor = options.AfterSeq ?? 0;

        if (options.SnapshotOnly)
        {
            var snapshotState = new SweepState();
            await foreach (var evt in SweepSealedArchiveAsync(options, matcher, cursor, snapshotState, cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }

            yield break;
        }

        var stalls = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sweep = new SweepState();
            await foreach (var evt in SweepSealedArchiveAsync(options, matcher, cursor, sweep, cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }

            var cutover = Math.Max(sweep.SealedTip, cursor);
            long resume;
            var tooOld = false;

            var live = await CreateLiveSessionAsync(options, cutover, cutover, cancellationToken).ConfigureAwait(false);
            try
            {
                var enumerator = live.RunAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
                try
                {
                    while (true)
                    {
                        bool hasNext;
                        try
                        {
                            hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                        }
                        catch (JetstreamV2Exception ex) when (ex.ErrorName == JetstreamV2ErrorNames.CursorTooOld)
                        {
                            _logger.LogWarning("Jetstream live cursor too old; re-entering archive backfill: {Message}", ex.Message);
                            tooOld = true;
                            break;
                        }

                        if (!hasNext)
                        {
                            break;
                        }

                        var evt = enumerator.Current;

                        if (matcher.WantsEvent(evt))
                        {
                            yield return evt;
                        }
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }

                resume = live.LastSeq;
            }
            finally
            {
                live.Dispose();
            }

            if (!tooOld)
            {
                yield break;
            }

            matcher.SetAfterSeq(resume);

            if (resume <= cursor)
            {
                stalls++;
                if (stalls >= MaxRebackfillStalls)
                {
                    throw new JetstreamV2Exception(
                        $"re-backfill made no progress after {stalls} cursor-too-old cycles at seq {resume}");
                }
            }
            else
            {
                stalls = 0;
            }

            cursor = resume;
        }
    }

    private async IAsyncEnumerable<JetstreamV2Event> TailLiveAsync(
        JetstreamV2SubscribeOptions options,
        JetstreamV2Matcher matcher,
        long? cursor,
        long dedupFloor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var live = await CreateLiveSessionAsync(options, cursor, dedupFloor, cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var evt in live.RunAsync(cancellationToken).ConfigureAwait(false))
            {
                if (matcher.WantsEvent(evt))
                {
                    yield return evt;
                }
            }
        }
        finally
        {
            live.Dispose();
        }
    }

    private async Task<JetstreamV2LiveSession> CreateLiveSessionAsync(
        JetstreamV2SubscribeOptions options,
        long? cursor,
        long dedupFloor,
        CancellationToken cancellationToken)
    {
        byte[]? dictionary = null;
        if (_clientOptions.EnableCompression)
        {
            dictionary = await FetchDictionaryAsync(cancellationToken).ConfigureAwait(false);
        }

        return new JetstreamV2LiveSession(new JetstreamV2LiveSessionConfig
        {
            BaseUri = _baseUri,
            Cursor = cursor,
            DedupFloor = dedupFloor,
            Kinds = KindStrings(options.Kinds),
            Collections = options.Collections ?? (IReadOnlyList<string>)Array.Empty<string>(),
            Dids = options.Dids ?? (IReadOnlyList<string>)Array.Empty<string>(),
            MaxMessageSizeBytes = options.MaxMessageSizeBytes,
            ReadLimitBytes = _clientOptions.ReadLimitBytes,
            BackoffMin = _clientOptions.ReconnectBackoffMin,
            BackoffMax = _clientOptions.ReconnectBackoffMax,
            ZstdDictionary = dictionary,
            RefetchDictionary = _clientOptions.EnableCompression ? FetchDictionaryAsync : null,
            Transport = _transport,
            Logger = _logger,
        });
    }

    private async Task<byte[]?> FetchDictionaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _transport.GetZstdDictionaryAsync(null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("getZstdDictionary failed ({Error}); the live tail will be uncompressed", ex.Message);
            return null;
        }
    }

    internal static IReadOnlyList<string> KindStrings(IReadOnlyList<JetstreamV2EventKind>? kinds)
    {
        if (kinds == null || kinds.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(kinds.Count);
        foreach (var kind in kinds)
        {
            var value = kind switch
            {
                JetstreamV2EventKind.Commit => "commit",
                JetstreamV2EventKind.Identity => "identity",
                JetstreamV2EventKind.Account => "account",
                JetstreamV2EventKind.Sync => "sync",
                _ => throw new ArgumentException($"unknown event kind {kind}"),
            };
            if (!result.Contains(value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private sealed class SweepState
    {
        /// <summary>The pinned sealed-archive tip (the cutover cursor), set when the first page is planned.</summary>
        public long SealedTip;
    }

    private async IAsyncEnumerable<JetstreamV2Event> SweepSealedArchiveAsync(
        JetstreamV2SubscribeOptions options,
        JetstreamV2Matcher matcher,
        long startCursor,
        SweepState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursor = startCursor;
        var pinned = false;
        long sealedTip = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var input = new JetstreamV2PlanSnapshotInput
            {
                Kinds = ToListOrNull(KindStrings(options.Kinds)),
                Dids = ToListOrNull(options.Dids),
                Collections = ToListOrNull(options.Collections),
                AfterSeq = cursor > 0 ? cursor : null,
                BeforeSeq = pinned ? sealedTip : options.BeforeSeq,
            };

            var output = await _transport.PlanSnapshotAsync(input, cancellationToken).ConfigureAwait(false);
            var plan = JetstreamV2PlanConverter.Convert(output);

            if (!pinned)
            {
                sealedTip = plan.SealedTipSeq;
                pinned = true;
                state.SealedTip = sealedTip;
                _logger.LogInformation(
                    "Jetstream backfill sweep: cursor {Cursor}, sealed tip {SealedTip}", cursor, sealedTip);
            }

            await foreach (var evt in DownloadPlanAsync(plan.Segments, matcher, cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }

            var previous = cursor;
            cursor = plan.PlannedThroughSeq;
            if (cursor >= sealedTip)
            {
                // Whole sealed archive (startCursor, sealedTip] consumed. An empty archive is
                // sealedTip == 0 and terminates here on the first page.
                yield break;
            }

            if (cursor <= previous)
            {
                // A stale, buggy, or hostile server returning a non-advancing continuation
                // cursor would reissue an identical request forever; fail instead of spinning.
                throw new JetstreamV2Exception(
                    $"planSnapshot made no progress: afterSeq={previous} plannedThroughSeq={cursor} sealedTipSeq={sealedTip}");
            }
        }
    }

    private async IAsyncEnumerable<JetstreamV2Event> DownloadPlanAsync(
        IReadOnlyList<JetstreamPlannedSegment> segments,
        JetstreamV2Matcher matcher,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var windowSize = Math.Max(2, _clientOptions.DownloadConcurrency);
        var window = new Queue<Task<List<JetstreamV2Event>>>();
        Task<byte[]>? prefetch = null;
        var prefetchIndex = -1;

        for (var i = 0; i < segments.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = segments[i];

            byte[]? segmentBytes = null;
            if (entry.Mode == JetstreamSegmentPlanMode.WholeSegment)
            {
                var download = prefetchIndex == i && prefetch != null
                    ? prefetch
                    : _transport.GetSegmentAsync(entry.Name, entry.Checksum, cancellationToken);
                prefetch = null;
                prefetchIndex = -1;
                segmentBytes = await download.ConfigureAwait(false);
            }

            if (prefetch == null && i + 1 < segments.Count &&
                segments[i + 1].Mode == JetstreamSegmentPlanMode.WholeSegment)
            {
                var next = segments[i + 1];
                prefetch = Task.Run(() => _transport.GetSegmentAsync(next.Name, next.Checksum, cancellationToken), cancellationToken);
                prefetchIndex = i + 1;
            }

            if (entry.Mode == JetstreamSegmentPlanMode.WholeSegment)
            {
                var header = JetstreamSegmentFormat.ReadHeader(segmentBytes);
                for (var blockIndex = 0; blockIndex < header.BlockCount; blockIndex++)
                {
                    var bytes = segmentBytes!;
                    var capturedHeader = header;
                    var capturedIndex = blockIndex;
                    window.Enqueue(Task.Run(
                        () =>
                        {
                            var frame = JetstreamSegmentFormat.GetBlockFrame(bytes, capturedHeader, capturedIndex);
                            return DecodeAndConvertBlock(frame, matcher);
                        },
                        cancellationToken));

                    while (window.Count >= windowSize)
                    {
                        foreach (var evt in await window.Dequeue().ConfigureAwait(false))
                        {
                            yield return evt;
                        }
                    }
                }
            }
            else
            {
                var name = entry.Name;
                foreach (var range in entry.Blocks)
                {
                    for (var blockIndex = range.First; ; blockIndex++)
                    {
                        var capturedIndex = blockIndex;
                        window.Enqueue(Task.Run(
                            async () =>
                            {
                                var frame = await _transport.GetBlockAsync(name, capturedIndex, cancellationToken).ConfigureAwait(false);
                                return DecodeAndConvertBlock(frame, matcher);
                            },
                            cancellationToken));

                        while (window.Count >= windowSize)
                        {
                            foreach (var evt in await window.Dequeue().ConfigureAwait(false))
                            {
                                yield return evt;
                            }
                        }

                        if (blockIndex == range.Last)
                        {
                            break;
                        }
                    }
                }
            }
        }

        while (window.Count > 0)
        {
            foreach (var evt in await window.Dequeue().ConfigureAwait(false))
            {
                yield return evt;
            }
        }
    }

    private List<JetstreamV2Event> DecodeAndConvertBlock(byte[] frame, JetstreamV2Matcher matcher)
    {
        var rows = JetstreamSegmentFormat.DecodeBlockFrame(frame);
        var events = new List<JetstreamV2Event>(rows.Count);
        foreach (var row in rows)
        {
            if (!matcher.WantsRow(row))
            {
                continue;
            }

            try
            {
                events.Add(JetstreamV2RecordDecoder.ConvertRow(row));
            }
            catch (JetstreamV2Exception ex)
            {
                _logger.LogWarning("Jetstream archive row skipped: {Error}", ex.Message);
            }
        }

        return events;
    }

    private static List<string>? ToListOrNull(IReadOnlyList<string>? values)
    {
        if (values == null || values.Count == 0)
        {
            return null;
        }

        return new List<string>(values);
    }
}
