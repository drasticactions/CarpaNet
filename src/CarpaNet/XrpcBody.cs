using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet;

/// <summary>
/// The body of an XRPC procedure call.
/// </summary>
/// <remarks>
/// A body can be sent more than once (for a retry after a token refresh) when it is held in
/// memory or comes from a seekable stream. A body from a non-seekable stream is sent once,
/// and a request that fails with 401 is not retried.
/// </remarks>
public sealed class XrpcBody
{
    private readonly byte[]? _bytes;
    private readonly Stream? _stream;
    private readonly long _startPosition;
    private bool _used;

    private XrpcBody(byte[]? bytes, Stream? stream, string contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            throw new ArgumentException("Content type cannot be null or empty.", nameof(contentType));
        }

        _bytes = bytes;
        _stream = stream;
        _startPosition = stream != null && stream.CanSeek ? stream.Position : 0;
        ContentType = contentType;
    }

    /// <summary>
    /// Gets the MIME type of the body.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Gets whether the body can be sent again.
    /// </summary>
    public bool IsReplayable => _bytes != null || (_stream?.CanSeek ?? false);

    /// <summary>
    /// Creates a body from bytes.
    /// </summary>
    /// <param name="data">The body bytes.</param>
    /// <param name="contentType">The MIME type.</param>
    public static XrpcBody FromBytes(byte[] data, string contentType)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return new XrpcBody(data, null, contentType);
    }

    /// <summary>
    /// Creates a body that streams from <paramref name="stream"/>, starting at its current position.
    /// The stream is not disposed.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="contentType">The MIME type.</param>
    public static XrpcBody FromStream(Stream stream, string contentType)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        return new XrpcBody(null, stream, contentType);
    }

    /// <summary>
    /// Creates a JSON body.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The serialization metadata for <typeparamref name="T"/>.</param>
    public static XrpcBody FromJson<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        if (typeInfo == null)
        {
            throw new ArgumentNullException(nameof(typeInfo));
        }

        return new XrpcBody(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo), null, "application/json");
    }

    /// <summary>
    /// Creates the HTTP content for one send. A stream body is rewound to its starting position
    /// when it is sent again. Used by <see cref="IXrpcRequestClient"/> implementations.
    /// </summary>
    /// <returns>The content, or null when the body was already sent and cannot be replayed.</returns>
    public HttpContent? CreateContent()
    {
        HttpContent content;
        if (_bytes != null)
        {
            content = new ByteArrayContent(_bytes);
        }
        else
        {
            if (_used)
            {
                if (!_stream!.CanSeek)
                {
                    return null;
                }

                _stream.Position = _startPosition;
            }

            content = new StreamContent(new NonDisposingStream(_stream!));
        }

        _used = true;
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContentType);
        return content;
    }

    /// <summary>
    /// Wraps a caller-owned stream so that disposing the request does not dispose it.
    /// </summary>
    private sealed class NonDisposingStream : Stream
    {
        private readonly Stream _inner;

        public NonDisposingStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);

#if NET8_0_OR_GREATER
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
#endif

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
