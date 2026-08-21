using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ZstdSharp;

namespace CarpaNet.Jetstream;

/// <summary>
/// Configuration for one <see cref="JetstreamV2LiveSession"/>.
/// </summary>
internal sealed class JetstreamV2LiveSessionConfig
{
    public Uri BaseUri { get; set; } = null!;

    /// <summary>
    /// The initial wire resume point sent as ?cursor= on the first connection (the server
    /// replays inclusively; the session's own seq dedup drops the overlap). Null omits the
    /// parameter so the server starts at the live tip.
    /// </summary>
    public long? Cursor { get; set; }

    /// <summary>
    /// Seeds the dedup floor: the highest seq the caller already holds. 0 means nothing was
    /// delivered yet, so the first real event (seq >= 1) always passes.
    /// </summary>
    public long DedupFloor { get; set; }

    public IReadOnlyList<string> Kinds { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Collections { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Dids { get; set; } = Array.Empty<string>();

    public long? MaxMessageSizeBytes { get; set; }

    public int ReadLimitBytes { get; set; }

    public TimeSpan BackoffMin { get; set; }

    public TimeSpan BackoffMax { get; set; }

    /// <summary>The dict-zstd dictionary blob, or null for an uncompressed tail.</summary>
    public byte[]? ZstdDictionary { get; set; }

    /// <summary>Re-fetches the server's current dictionary after a rotation rejection; null disables in-place recovery.</summary>
    public Func<CancellationToken, Task<byte[]?>>? RefetchDictionary { get; set; }

    /// <summary>Classifies failed handshakes via a plain HTTP probe of the same URL.</summary>
    public JetstreamV2Transport Transport { get; set; } = null!;

    public ILogger Logger { get; set; } = null!;
}

/// <summary>
/// Tails the live network.bsky.jetstream.subscribeEvents websocket: dial, read, decode,
/// deduplicate the at-least-once overlap by seq, and reconnect with bounded exponential
/// backoff. Pre-upgrade CursorTooOld and InvalidRequest rejections are terminal for the
/// session (thrown as <see cref="JetstreamV2Exception"/>); a rotated zstd dictionary is
/// recovered in place; everything else reconnects.
/// </summary>
internal sealed class JetstreamV2LiveSession : IDisposable
{
    private const string Subprotocol = "xrpc.v1.json";

    private readonly JetstreamV2LiveSessionConfig _cfg;

    private Decompressor? _decompressor;
    private uint _dictionaryId;
    private bool _seenAny;

    public JetstreamV2LiveSession(JetstreamV2LiveSessionConfig cfg)
    {
        _cfg = cfg;
        LastSeq = cfg.DedupFloor;
        InstallDictionary(cfg.ZstdDictionary, logFailure: true);
    }

    /// <summary>
    /// The highest seq delivered, or the seeded dedup floor when nothing was delivered yet.
    /// The engine reads it after enumeration ends to resume a re-backfill.
    /// </summary>
    public long LastSeq { get; private set; }

    /// <summary>
    /// Runs the tail until cancellation, yielding decoded events in delivery order.
    /// </summary>
    public async IAsyncEnumerable<JetstreamV2Event> RunAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var backoff = _cfg.BackoffMin;
        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? webSocket = null;
            string? failure = null;
            try
            {
                webSocket = await ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (JetstreamV2Exception ex) when (
                ex.ErrorName == JetstreamV2ErrorNames.CursorTooOld ||
                ex.ErrorName == JetstreamV2ErrorNames.InvalidRequest)
            {
                // Terminal for the session: the cursor will not become valid by retrying, and
                // filters are immutable per connection. The engine decides whether a
                // CursorTooOld can be recovered by re-entering backfill.
                throw;
            }
            catch (JetstreamV2Exception ex) when (ex.ErrorName == JetstreamV2ErrorNames.UnknownZstdDictionary)
            {
                // The server rotated its dictionary out from under us. Recoverable in place:
                // refresh (or shed) the dictionary before the reconnect below.
                await RefreshDictionaryAsync(cancellationToken).ConfigureAwait(false);
                failure = ex.Message;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }

            if (webSocket != null)
            {
                var seqBefore = LastSeq;
                var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
                try
                {
                    while (true)
                    {
                        JetstreamV2Event? next = null;
                        try
                        {
                            var turn = await ReadNextAsync(webSocket, buffer, cancellationToken).ConfigureAwait(false);
                            buffer = turn.Buffer;
                            if (turn.SessionEnded)
                            {
                                failure = turn.Reason;
                                break;
                            }

                            next = turn.Event;
                        }
                        catch (OperationCanceledException)
                        {
                            yield break;
                        }
                        catch (Exception ex)
                        {
                            failure = ex.Message;
                            break;
                        }

                        // Deduplicate the at-least-once reconnect overlap: skip anything at or
                        // below the highest seq already delivered.
                        if (next == null || next.Seq <= LastSeq)
                        {
                            continue;
                        }

                        LastSeq = next.Seq;
                        _seenAny = true;
                        yield return next;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    webSocket.Dispose();
                }

                // A session that delivered new events is healthy; reset backoff so a
                // long-lived connection that finally drops reconnects promptly.
                if (LastSeq != seqBefore)
                {
                    backoff = _cfg.BackoffMin;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            if (failure != null)
            {
                _cfg.Logger.LogWarning("Jetstream live tail reconnecting: {Reason}", failure);
            }

            try
            {
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            backoff = backoff.TotalMilliseconds * 2 > _cfg.BackoffMax.TotalMilliseconds
                ? _cfg.BackoffMax
                : TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * 2);
        }
    }

    public void Dispose()
    {
        _decompressor?.Dispose();
        _decompressor = null;
    }

    private readonly struct SessionTurn
    {
        public SessionTurn(JetstreamV2Event? evt, bool ended, string? reason, byte[] buffer)
        {
            Event = evt;
            SessionEnded = ended;
            Reason = reason;
            Buffer = buffer;
        }

        public JetstreamV2Event? Event { get; }

        public bool SessionEnded { get; }

        public string? Reason { get; }

        /// <summary>The (possibly re-rented, grown) receive buffer, handed back to the caller.</summary>
        public byte[] Buffer { get; }
    }

    /// <summary>
    /// Reads frames until one yields an event or ends the session. Info advisories, unknown
    /// $types, malformed frames, and stray binary frames are logged/skipped here.
    /// </summary>
    private async Task<SessionTurn> ReadNextAsync(ClientWebSocket webSocket, byte[] buffer, CancellationToken cancellationToken)
    {
        while (true)
        {
            var (messageType, count, grown) = await ReceiveFullMessageAsync(webSocket, buffer, cancellationToken).ConfigureAwait(false);
            buffer = grown;
            if (messageType == WebSocketMessageType.Close)
            {
                return new SessionTurn(null, true, "server closed the connection", buffer);
            }

            ReadOnlyMemory<byte> frame;
            if (messageType == WebSocketMessageType.Binary)
            {
                if (_decompressor == null)
                {
                    // Jetstream v2 frames are text JSON unless dict-zstd was negotiated;
                    // ignore stray binary.
                    continue;
                }

                byte[] decompressed;
                try
                {
                    var contentSize = Decompressor.GetDecompressedSize(buffer.AsSpan(0, count));
                    if (contentSize > (ulong)_cfg.ReadLimitBytes)
                    {
                        throw new JetstreamV2Exception(
                            $"compressed frame would expand to {contentSize} bytes, over the {_cfg.ReadLimitBytes} byte read limit");
                    }

                    decompressed = _decompressor.Unwrap(buffer.AsSpan(0, count)).ToArray();
                }
                catch (Exception ex)
                {
                    // Upstream input, never crash: surface and keep the tail.
                    _cfg.Logger.LogWarning("Jetstream zstd frame decode failed: {Error}", ex.Message);
                    continue;
                }

                frame = decompressed;
            }
            else
            {
                frame = new ReadOnlyMemory<byte>(buffer, 0, count);
            }

            var result = JetstreamV2FrameDecoder.Decode(frame.Span);
            switch (result.Kind)
            {
                case JetstreamV2FrameKind.Event:
                    return new SessionTurn(result.Event, false, null, buffer);
                case JetstreamV2FrameKind.Info:
                    // An advisory (e.g. OutdatedCursor on a clamped timestamp resume). Not an
                    // event: no seq, no cursor advance. Operator-relevant, so log it.
                    _cfg.Logger.LogInformation("Jetstream stream info: {Name} {Message}", result.Name, result.Message);
                    continue;
                case JetstreamV2FrameKind.StreamError:
                    // A terminal error frame: the server closes right after sending it. End
                    // the session so the reconnect loop backs off and resumes at LastSeq.
                    return new SessionTurn(null, true, $"stream error {result.Name}: {result.Message}", buffer);
                case JetstreamV2FrameKind.Malformed:
                    // One bad frame must not drop the tail.
                    _cfg.Logger.LogWarning("Jetstream malformed frame: {Reason}", result.Name);
                    continue;
                default:
                    continue;
            }
        }
    }

    private async Task<(WebSocketMessageType MessageType, int Count, byte[] Buffer)> ReceiveFullMessageAsync(
        ClientWebSocket webSocket, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var segment = new ArraySegment<byte>(buffer, total, buffer.Length - total);
            var result = await webSocket.ReceiveAsync(segment, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (WebSocketMessageType.Close, 0, buffer);
            }

            total += result.Count;
            if (result.EndOfMessage)
            {
                return (result.MessageType, total, buffer);
            }

            if (total >= buffer.Length)
            {
                if (buffer.Length >= _cfg.ReadLimitBytes)
                {
                    throw new JetstreamV2Exception($"message exceeds the {_cfg.ReadLimitBytes} byte read limit");
                }

                var next = ArrayPool<byte>.Shared.Rent(Math.Min(buffer.Length * 2, _cfg.ReadLimitBytes));
                Array.Copy(buffer, next, total);
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = next;
            }
        }
    }

    private async Task<ClientWebSocket> ConnectAsync(CancellationToken cancellationToken)
    {
        var wsUri = BuildSubscribeUri(webSocketScheme: true);
        var webSocket = new ClientWebSocket();
        try
        {
            webSocket.Options.AddSubProtocol(Subprotocol);
            await webSocket.ConnectAsync(wsUri, cancellationToken).ConfigureAwait(false);
            return webSocket;
        }
        catch (Exception ex) when (ex is WebSocketException && !cancellationToken.IsCancellationRequested)
        {
            webSocket.Dispose();

            // .NET does not expose the pre-upgrade HTTP response body, so classify the
            // rejection by re-issuing the request as a plain HTTP GET: the server validates
            // parameters before upgrading, so the probe reproduces the XRPC error envelope.
            var envelope = await _cfg.Transport
                .ProbeHandshakeErrorAsync(BuildSubscribeUri(webSocketScheme: false), cancellationToken)
                .ConfigureAwait(false);
            if (envelope?.Error != null && envelope.Error != JetstreamV2ErrorNames.ServiceUnavailable)
            {
                throw new JetstreamV2Exception($"{envelope.Error}: {envelope.Message ?? wsUri.ToString()}", envelope.Error);
            }

            throw new JetstreamV2Exception($"websocket connect failed: {ex.Message}", (Exception?)ex);
        }
        catch
        {
            webSocket.Dispose();
            throw;
        }
    }

    private Uri BuildSubscribeUri(bool webSocketScheme)
    {
        var query = new List<KeyValuePair<string, string>>();

        // Once any event has been delivered, resume each new session at LastSeq — re-anchoring
        // at the reconnect-time tip would silently drop events produced while disconnected.
        // Before any delivery use the configured start: omit the parameter entirely for a
        // from-tip start (distinct from cursor=0, which replays everything).
        if (_seenAny)
        {
            query.Add(new KeyValuePair<string, string>("cursor", LastSeq.ToString(CultureInfo.InvariantCulture)));
        }
        else if (_cfg.Cursor != null)
        {
            query.Add(new KeyValuePair<string, string>("cursor", _cfg.Cursor.Value.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var kind in _cfg.Kinds)
        {
            query.Add(new KeyValuePair<string, string>("kinds", kind));
        }

        foreach (var collection in _cfg.Collections)
        {
            query.Add(new KeyValuePair<string, string>("collections", collection));
        }

        foreach (var did in _cfg.Dids)
        {
            query.Add(new KeyValuePair<string, string>("dids", did));
        }

        if (_cfg.MaxMessageSizeBytes is > 0)
        {
            query.Add(new KeyValuePair<string, string>(
                "maxMessageSizeBytes", _cfg.MaxMessageSizeBytes.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (_decompressor != null)
        {
            query.Add(new KeyValuePair<string, string>(
                "zstdDictionary", _dictionaryId.ToString(CultureInfo.InvariantCulture)));
        }

        var uri = JetstreamV2Transport.BuildXrpcUri(_cfg.BaseUri, JetstreamV2Transport.SubscribeEventsNsid, query);
        if (!webSocketScheme)
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        if (builder.Scheme == "http")
        {
            builder.Scheme = "ws";
        }
        else if (builder.Scheme == "https")
        {
            builder.Scheme = "wss";
        }

        return builder.Uri;
    }

    private void InstallDictionary(byte[]? dictionary, bool logFailure)
    {
        if (dictionary == null)
        {
            return;
        }

        if (!JetstreamZstdDictionary.TryParseId(dictionary, out var id))
        {
            if (logFailure)
            {
                _cfg.Logger.LogWarning("Invalid zstd dictionary blob; continuing with an uncompressed live tail");
            }

            return;
        }

        try
        {
            var decompressor = new Decompressor();
            decompressor.LoadDictionary(dictionary);
            _decompressor?.Dispose();
            _decompressor = decompressor;
            _dictionaryId = id;
        }
        catch (Exception ex)
        {
            if (logFailure)
            {
                _cfg.Logger.LogWarning("Zstd decompressor construction failed ({Error}); continuing uncompressed", ex.Message);
            }
        }
    }

    /// <summary>
    /// Recovers from a server-side dictionary rotation: re-fetch the current dictionary and
    /// swap the decompressor so the next dial negotiates the new ID. When the refetch is
    /// unavailable, fails, or returns the very ID just rejected (a mixed-version fleet), shed
    /// the opt-in and continue uncompressed — compression is an optimization; the tail must
    /// keep flowing.
    /// </summary>
    private async Task RefreshDictionaryAsync(CancellationToken cancellationToken)
    {
        if (_decompressor == null)
        {
            return;
        }

        var rejectedId = _dictionaryId;
        if (_cfg.RefetchDictionary != null)
        {
            var blob = await _cfg.RefetchDictionary(cancellationToken).ConfigureAwait(false);
            if (blob != null && JetstreamZstdDictionary.TryParseId(blob, out var newId) && newId != rejectedId)
            {
                InstallDictionary(blob, logFailure: false);
                if (_dictionaryId == newId)
                {
                    _cfg.Logger.LogInformation(
                        "Jetstream zstd dictionary rotated: rejected {RejectedId}, now using {NewId}", rejectedId, newId);
                    return;
                }
            }
        }

        _decompressor.Dispose();
        _decompressor = null;
        _dictionaryId = 0;
        _cfg.Logger.LogWarning(
            "Jetstream zstd dictionary {RejectedId} rejected and refetch unavailable; continuing uncompressed", rejectedId);
    }
}
