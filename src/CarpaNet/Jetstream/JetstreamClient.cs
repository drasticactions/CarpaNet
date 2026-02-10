using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;

namespace CarpaNet.Jetstream;

/// <summary>
/// Client for consuming the Jetstream event stream (JSON over WebSocket).
/// Jetstream provides a lightweight alternative to the full ATProtocol firehose,
/// delivering decoded records as JSON instead of raw CBOR/CAR blocks.
/// </summary>
public sealed class JetstreamClient : IDisposable
{
    private readonly Uri _baseUri;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Decompressor? _decompressor;

    private ClientWebSocket? _webSocket;
    private bool _disposed;

    /// <summary>
    /// Default buffer size for receiving WebSocket frames.
    /// </summary>
    public int BufferSize { get; set; } = 512 * 1024; // 512KB default

    /// <summary>
    /// Creates a new JetstreamClient.
    /// </summary>
    /// <param name="baseUri">The Jetstream instance URI (e.g., https://jetstream1.us-east.bsky.network).</param>
    /// <param name="zstdDictionary">Optional zstd dictionary bytes for decompressing binary frames.
    /// Required when using <see cref="JetstreamSubscribeOptions.Compress"/> = true.
    /// The dictionary can be obtained from the Jetstream repository at
    /// https://github.com/bluesky-social/jetstream/raw/main/pkg/models/zstd_dictionary</param>
    public JetstreamClient(Uri baseUri, byte[]? zstdDictionary = null)
        : this(baseUri, JetstreamJsonContext.Default.Options, zstdDictionary)
    {
    }

    /// <summary>
    /// Creates a new JetstreamClient with custom JSON options.
    /// </summary>
    /// <param name="baseUri">The Jetstream instance URI.</param>
    /// <param name="jsonOptions">Custom JSON serializer options.</param>
    /// <param name="zstdDictionary">Optional zstd dictionary bytes for decompressing binary frames.</param>
    public JetstreamClient(Uri baseUri, JsonSerializerOptions jsonOptions, byte[]? zstdDictionary = null)
    {
        _baseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
        _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));

        if (zstdDictionary != null)
        {
            _decompressor = new Decompressor();
            _decompressor.LoadDictionary(zstdDictionary);
        }
    }

    /// <summary>
    /// Subscribes to the Jetstream event stream and yields deserialized events.
    /// </summary>
    /// <param name="options">Optional subscription options for filtering and cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of Jetstream events.</returns>
    public async IAsyncEnumerable<JetstreamEvent> SubscribeAsync(
        JetstreamSubscribeOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var uri = BuildWebSocketUri(options);

        _webSocket = new ClientWebSocket();
        try
        {
            await _webSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                while (!cancellationToken.IsCancellationRequested &&
                       _webSocket.State == WebSocketState.Open)
                {
                    var result = await ReceiveFullMessageAsync(buffer, cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        yield break;
                    }

                    JetstreamEvent? evt = null;

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = buffer.AsSpan(0, result.Count);
                        evt = JsonSerializer.Deserialize(json, JetstreamJsonContext.Default.JetstreamEvent);
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        if (_decompressor == null)
                        {
                            throw new InvalidOperationException(
                                "Received zstd-compressed binary frame but no zstd dictionary was provided. " +
                                "Pass the Jetstream zstd dictionary to the JetstreamClient constructor to enable compression support.");
                        }

                        var compressed = buffer.AsSpan(0, result.Count);
                        var decompressed = _decompressor.Unwrap(compressed);
                        evt = JsonSerializer.Deserialize(decompressed, JetstreamJsonContext.Default.JetstreamEvent);
                    }

                    if (evt != null)
                    {
                        yield return evt;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Ignore close errors
                }
            }

            _webSocket.Dispose();
            _webSocket = null;
        }
    }

    /// <summary>
    /// Subscribes to the Jetstream event stream and yields raw message bytes.
    /// Use this when you need to handle decompression or custom deserialization.
    /// </summary>
    /// <param name="options">Optional subscription options for filtering and cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of (message type, message bytes) tuples.</returns>
    public async IAsyncEnumerable<(WebSocketMessageType MessageType, ReadOnlyMemory<byte> Data)> SubscribeRawAsync(
        JetstreamSubscribeOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var uri = BuildWebSocketUri(options);

        _webSocket = new ClientWebSocket();
        try
        {
            await _webSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                while (!cancellationToken.IsCancellationRequested &&
                       _webSocket.State == WebSocketState.Open)
                {
                    var result = await ReceiveFullMessageAsync(buffer, cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        yield break;
                    }

                    var data = new byte[result.Count];
                    Buffer.BlockCopy(buffer, 0, data, 0, result.Count);
                    yield return (result.MessageType, data);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Ignore close errors
                }
            }

            _webSocket.Dispose();
            _webSocket = null;
        }
    }

    /// <summary>
    /// Sends an options_update message to dynamically change subscription filters.
    /// Only valid after connecting with <see cref="JetstreamSubscribeOptions.RequireHello"/> = true
    /// or to update filters on an active connection.
    /// </summary>
    /// <param name="update">The options update to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SendOptionsUpdateAsync(JetstreamOptionsUpdate update, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected");
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(update, JetstreamJsonContext.Default.JetstreamOptionsUpdate);
        await _webSocket.SendAsync(
            new ArraySegment<byte>(json),
            WebSocketMessageType.Text,
            true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WebSocketReceiveResult> ReceiveFullMessageAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var totalBytes = 0;
        WebSocketReceiveResult result;

        do
        {
            var segment = new ArraySegment<byte>(buffer, totalBytes, buffer.Length - totalBytes);
            result = await _webSocket!.ReceiveAsync(segment, cancellationToken).ConfigureAwait(false);
            totalBytes += result.Count;

            if (totalBytes >= buffer.Length && !result.EndOfMessage)
            {
                throw new InvalidOperationException("Jetstream message too large for buffer");
            }
        } while (!result.EndOfMessage);

        return new WebSocketReceiveResult(totalBytes, result.MessageType, true, result.CloseStatus, result.CloseStatusDescription);
    }

    private Uri BuildWebSocketUri(JetstreamSubscribeOptions? options)
    {
        var uriBuilder = new UriBuilder(_baseUri)
        {
            Path = "/subscribe"
        };

        if (options != null)
        {
            var queryParams = new List<string>();

            foreach (var param in options.ToQueryParameters())
            {
                queryParams.Add($"{Uri.EscapeDataString(param.Key)}={Uri.EscapeDataString(param.Value)}");
            }

            if (queryParams.Count > 0)
            {
                uriBuilder.Query = string.Join("&", queryParams);
            }
        }

        // Ensure WebSocket scheme
        if (uriBuilder.Scheme == "http")
        {
            uriBuilder.Scheme = "ws";
        }
        else if (uriBuilder.Scheme == "https")
        {
            uriBuilder.Scheme = "wss";
        }

        return uriBuilder.Uri;
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

    /// <summary>
    /// Disposes the client and closes any open connections.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _decompressor?.Dispose();
        _webSocket?.Dispose();
    }
}
