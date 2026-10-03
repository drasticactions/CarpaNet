using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.Http;

/// <summary>
/// A read-only stream wrapper that reports how many bytes have been read, for upload progress.
/// </summary>
/// <remarks>
/// Wrap the content passed to an upload (for example
/// <see cref="CarpaNet.Blob.ATProtoClientBlobExtensions.UploadBlobAsync(IATProtoClient, Stream, string, CancellationToken)"/>
/// or a generated binary procedure). The reported value is the position in the inner stream, so it
/// drops back when the stream is rewound for a retry.
/// </remarks>
public sealed class ProgressReportingStream : Stream
{
    private readonly Stream _inner;
    private readonly IProgress<long> _progress;
    private readonly bool _leaveOpen;
    private long _bytesRead;

    /// <summary>
    /// Creates a progress-reporting wrapper.
    /// </summary>
    /// <param name="inner">The stream to read.</param>
    /// <param name="progress">Receives the total number of bytes read after each read.</param>
    /// <param name="leaveOpen">Whether disposing the wrapper leaves <paramref name="inner"/> open.</param>
    public ProgressReportingStream(Stream inner, IProgress<long> progress, bool leaveOpen = true)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _leaveOpen = leaveOpen;
        _bytesRead = inner.CanSeek ? inner.Position : 0;
    }

    /// <summary>
    /// Gets the number of bytes read so far.
    /// </summary>
    public long BytesRead => _bytesRead;

    /// <inheritdoc/>
    public override bool CanRead => _inner.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => _inner.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => _inner.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _inner.Position;
        set
        {
            _inner.Position = value;
            Report(value);
        }
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Advance(read);
        return read;
    }

    /// <inheritdoc/>
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        Advance(read);
        return read;
    }

#if NET8_0_OR_GREATER
    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Advance(read);
        return read;
    }
#endif

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        var position = _inner.Seek(offset, origin);
        Report(position);
        return position;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Advance(int read)
    {
        if (read > 0)
        {
            Report(_bytesRead + read);
        }
    }

    private void Report(long bytesRead)
    {
        _bytesRead = bytesRead;
        _progress.Report(bytesRead);
    }
}
