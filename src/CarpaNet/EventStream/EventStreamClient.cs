using System;
using System.Buffers;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Cbor;

namespace CarpaNet.EventStream;

/// <summary>
/// WebSocket client for ATProtocol event streams (firehose).
/// - requires a CborSerializerContext with registered types.
/// </summary>
public sealed class EventStreamClient : IDisposable
{
    private readonly Uri _baseUri;
    private readonly CborSerializerContext _context;

    private ClientWebSocket? _webSocket;
    private bool _disposed;

    /// <summary>
    /// Default buffer size for receiving WebSocket frames.
    /// </summary>
    public int BufferSize { get; set; } = 1024 * 1024; // 1MB default

    /// <summary>
    /// Creates a new EventStreamClient with the default serializer context.
    /// </summary>
    /// <param name="baseUri">The base WebSocket URI (e.g., wss://bsky.network).</param>
    public EventStreamClient(Uri baseUri)
        : this(baseUri, CborSerializerContext.Default)
    {
    }

    /// <summary>
    /// Creates a new EventStreamClient with a custom serializer context.
    /// </summary>
    /// <param name="baseUri">The base WebSocket URI (e.g., wss://bsky.network).</param>
    /// <param name="context">The serializer context with registered types.</param>
    public EventStreamClient(Uri baseUri, CborSerializerContext context)
    {
        _baseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Subscribes to an event stream and yields deserialized messages.
    /// </summary>
    /// <typeparam name="TMessage">The message type. Must be registered in the serializer context.</typeparam>
    /// <param name="nsid">The NSID of the subscription endpoint.</param>
    /// <param name="parameters">Optional query parameters as key-value pairs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of messages.</returns>
    public async IAsyncEnumerable<TMessage> SubscribeAsync<TMessage>(
        string nsid,
        IEnumerable<KeyValuePair<string, string?>>? parameters = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var uri = BuildWebSocketUri(nsid, parameters);

        _webSocket = new ClientWebSocket();
        try
        {
            await _webSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

            await foreach (var frame in ReadFramesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (frame.Header.IsError)
                {
                    throw new EventStreamException(frame.Header);
                }

                if (frame.Header.IsInfo)
                {
                    // Skip info/keepalive frames
                    continue;
                }

                if (frame.Header.IsMessage && frame.Header.MessageType != null)
                {
                    // Try to resolve the actual type from the context
                    if (_context.TryGetTypeFromDiscriminatorWithSuffix(frame.Header.MessageType, out var messageType) && messageType != null)
                    {
                        // Deserialize using the resolved type (using memory overload for async compatibility)
                        if (_context.TryGetTypeInfo(messageType, out var typeInfo) && typeInfo != null)
                        {
                            var result = typeInfo.ReadObject(frame.PayloadBytes);
                            if (result is TMessage typedResult)
                            {
                                yield return typedResult;
                            }
                        }
                    }
                    else
                    {
                        // Fall back to direct deserialization if type is registered
                        var message = _context.Deserialize<TMessage>(frame.PayloadBytes);
                        if (message != null)
                        {
                            yield return message;
                        }
                    }
                }
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
    /// Subscribes to an event stream and yields raw frames.
    /// Use this when you need to handle message deserialization manually.
    /// </summary>
    /// <param name="nsid">The NSID of the subscription endpoint.</param>
    /// <param name="parameters">Optional query parameters as key-value pairs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of frames.</returns>
    public async IAsyncEnumerable<EventStreamFrame> SubscribeRawAsync(
        string nsid,
        IEnumerable<KeyValuePair<string, string?>>? parameters = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var uri = BuildWebSocketUri(nsid, parameters);

        _webSocket = new ClientWebSocket();
        try
        {
            await _webSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

            await foreach (var frame in ReadFramesAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
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
    /// Reads raw frames from the WebSocket connection.
    /// </summary>
    public async IAsyncEnumerable<EventStreamFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_webSocket == null)
        {
            throw new InvalidOperationException("WebSocket is not connected");
        }

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

                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    continue;
                }

                var frameData = buffer.AsMemory(0, result.Count);
                var frame = ParseFrame(frameData);
                yield return frame;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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
                throw new EventStreamException("Message too large for buffer");
            }
        } while (!result.EndOfMessage);

        return new WebSocketReceiveResult(totalBytes, result.MessageType, true, result.CloseStatus, result.CloseStatusDescription);
    }

    /// <summary>
    /// Parses a WebSocket frame containing two concatenated CBOR objects (header + payload).
    /// </summary>
    private EventStreamFrame ParseFrame(ReadOnlyMemory<byte> frameData)
    {
        // Frame format: [CBOR Header Object] + [CBOR Payload Object]
        var reader = new CborReader(frameData, CborConformanceMode.Lax, allowMultipleRootLevelValues: true);
        var initialLength = frameData.Length;

        // Read header
        var header = ParseHeader(ref reader);

        // The remaining bytes are the payload
        var bytesRead = initialLength - reader.BytesRemaining;
        var payloadBytes = frameData.Slice(bytesRead);

        return new EventStreamFrame(header, payloadBytes);
    }

    private static EventStreamHeader ParseHeader(ref CborReader reader)
    {
        var header = new EventStreamHeader();

        var count = reader.ReadStartMap();
        var remaining = count ?? int.MaxValue;

        while (remaining > 0 && reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();

            switch (key)
            {
                case "op":
                    header.Op = reader.ReadInt32();
                    break;
                case "t":
                    header.MessageType = reader.ReadTextString();
                    break;
                case "error":
                    header.Error = reader.ReadTextString();
                    break;
                case "message":
                    header.Message = reader.ReadTextString();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }

            remaining--;
        }

        reader.ReadEndMap();
        return header;
    }

    private Uri BuildWebSocketUri(string nsid, IEnumerable<KeyValuePair<string, string?>>? parameters)
    {
        var uriBuilder = new UriBuilder(_baseUri)
        {
            Path = $"/xrpc/{nsid}"
        };

        if (parameters != null)
        {
            var queryParams = new List<string>();

            foreach (var param in parameters)
            {
                if (param.Value != null)
                {
                    queryParams.Add($"{Uri.EscapeDataString(param.Key)}={Uri.EscapeDataString(param.Value)}");
                }
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
        _webSocket?.Dispose();
    }
}
