using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarpaNet.Jetstream;

/// <summary>
/// Client for Jetstream v2: the archive-backed ATProtocol event stream service
/// (github.com/bluesky-social/jetstream). One facade covers both transports — sealed history
/// over HTTP/XRPC (planSnapshot → getSegment/getBlock) and the live
/// network.bsky.jetstream.subscribeEvents websocket — and <see cref="SubscribeAsync"/> welds
/// them into a single managed stream: replay in seq order, cut over to live with no gap,
/// reconnect with backoff, resume at the last delivered seq, and recover from cursor and
/// compression-dictionary rejections automatically.
/// </summary>
/// <remarks>
/// For the legacy v1 /subscribe wire, use <see cref="JetstreamClient"/>. Servers running
/// Jetstream v2 serve both endpoints; this client speaks only the v2 wire.
/// </remarks>
public sealed class JetstreamV2Client : IDisposable
{
    private readonly JetstreamV2ClientOptions _options;
    private readonly HttpClient? _ownedHttpClient;
    private readonly JetstreamV2Transport _transport;
    private readonly JetstreamV2Engine _engine;
    private bool _disposed;

    /// <summary>
    /// Creates a new Jetstream v2 client.
    /// </summary>
    /// <param name="baseUri">The Jetstream instance URI (e.g. https://jetstream.us-east.bsky.network).</param>
    /// <param name="options">Optional client configuration.</param>
    public JetstreamV2Client(Uri baseUri, JetstreamV2ClientOptions? options = null)
    {
        if (baseUri == null)
        {
            throw new ArgumentNullException(nameof(baseUri));
        }

        _options = options ?? new JetstreamV2ClientOptions();
        _options.Validate();

        var httpClient = _options.HttpClient;
        if (httpClient == null)
        {
            // Bulk segment downloads are large transfers; a short wall-clock timeout would
            // prematurely kill them, so the owned client relies on cancellation tokens.
            _ownedHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            httpClient = _ownedHttpClient;
        }

        var logger = (ILogger?)_options.LoggerFactory?.CreateLogger("CarpaNet.Jetstream.JetstreamV2Client")
            ?? NullLogger.Instance;

        _transport = new JetstreamV2Transport(baseUri, httpClient, _options.ApiKey, _options.MaxDownloadAttempts, logger);
        _engine = new JetstreamV2Engine(baseUri, _options, _transport, logger);
    }

    /// <summary>
    /// Subscribes to the managed Jetstream v2 stream and yields events in seq order until
    /// cancellation. Depending on <paramref name="options"/> this is a pure live tail, a
    /// backfill of sealed history that cuts over to live, or a point-in-time archive snapshot
    /// (see <see cref="JetstreamV2SubscribeOptions"/>).
    /// </summary>
    /// <remarks>
    /// Delivery is at-least-once across every boundary; the client dedups by seq internally,
    /// but consumers that persist a cursor should treat re-delivery of a persisted seq as
    /// possible and fold idempotently. Recoverable conditions (reconnects, dictionary
    /// rotation, malformed frames, malformed rows) are logged and handled internally;
    /// unrecoverable ones throw <see cref="JetstreamV2Exception"/>.
    /// </remarks>
    /// <param name="options">Filters, cursors, and stream mode; null tails live from the current tip.</param>
    /// <param name="cancellationToken">Ends the stream when cancelled.</param>
    /// <returns>The managed event stream.</returns>
    public IAsyncEnumerable<JetstreamV2Event> SubscribeAsync(
        JetstreamV2SubscribeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var resolved = options ?? new JetstreamV2SubscribeOptions();
        resolved.Validate();
        return _engine.RunAsync(resolved, cancellationToken);
    }

    /// <summary>
    /// Calls network.bsky.jetstream.planSnapshot: builds a transport plan naming the sealed
    /// segments (or block ranges) that may contain events for the requested filters. The plan
    /// over-approximates; exact filtering happens after decode. Page by re-issuing with
    /// afterSeq = the previous page's <see cref="JetstreamSnapshotPlan.PlannedThroughSeq"/>.
    /// </summary>
    /// <param name="request">The filters and seq window to plan for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validated plan page.</returns>
    public async Task<JetstreamSnapshotPlan> PlanSnapshotAsync(
        JetstreamSnapshotPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var input = new JetstreamV2PlanSnapshotInput
        {
            Kinds = ToListOrNull(JetstreamV2Engine.KindStrings(request.Kinds)),
            Dids = ToListOrNull(request.Dids),
            Collections = ToListOrNull(request.Collections),
            AfterSeq = request.AfterSeq > 0 ? request.AfterSeq : null,
            BeforeSeq = request.BeforeSeq,
        };

        var output = await _transport.PlanSnapshotAsync(input, cancellationToken).ConfigureAwait(false);
        return JetstreamV2PlanConverter.Convert(output);
    }

    /// <summary>
    /// Downloads a whole sealed segment file via network.bsky.jetstream.getSegment. Decode it
    /// with <see cref="JetstreamSegmentFormat"/>.
    /// </summary>
    /// <param name="name">The segment filename from a plan (e.g. "seg_000000002a.jss").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw segment file bytes.</returns>
    public Task<byte[]> GetSegmentAsync(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Segment name is required.", nameof(name));
        }

        return _transport.GetSegmentAsync(name, null, cancellationToken);
    }

    /// <summary>
    /// Downloads a single stored block frame via network.bsky.jetstream.getBlock. The result
    /// is exactly the compressed frame <see cref="JetstreamSegmentFormat.DecodeBlockFrame(byte[])"/>
    /// accepts.
    /// </summary>
    /// <param name="segment">The sealed segment filename.</param>
    /// <param name="blockIndex">The zero-based block index within the segment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw zstd block frame.</returns>
    public Task<byte[]> GetBlockAsync(string segment, int blockIndex, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(segment))
        {
            throw new ArgumentException("Segment name is required.", nameof(segment));
        }

        if (blockIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blockIndex));
        }

        return _transport.GetBlockAsync(segment, blockIndex, cancellationToken);
    }

    /// <summary>
    /// Downloads a zstd compression dictionary via network.bsky.jetstream.getZstdDictionary.
    /// The blob is a structured dictionary (RFC 8878 §5), immutable for a given ID. The managed
    /// stream fetches and rotates dictionaries automatically when
    /// <see cref="JetstreamV2ClientOptions.EnableCompression"/> is set; this method exists for
    /// direct wire access.
    /// </summary>
    /// <param name="id">A specific dictionary ID, or null for the server's current one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The dictionary bytes.</returns>
    public Task<byte[]> GetZstdDictionaryAsync(long? id = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _transport.GetZstdDictionaryAsync(id, cancellationToken);
    }

    /// <summary>
    /// Disposes the client and any HTTP client it owns.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ownedHttpClient?.Dispose();
    }

    private static List<string>? ToListOrNull(IReadOnlyList<string>? values)
    {
        if (values == null || values.Count == 0)
        {
            return null;
        }

        return new List<string>(values);
    }

    private void ThrowIfDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
#endif
    }
}
