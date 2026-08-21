using System;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace CarpaNet.Jetstream;

/// <summary>
/// Configuration for a <see cref="JetstreamV2Client"/>. All properties have working defaults.
/// </summary>
public sealed class JetstreamV2ClientOptions
{
    private static readonly int DefaultConcurrency =
        Math.Max(4, Math.Min(32, Environment.ProcessorCount));

    /// <summary>
    /// HTTP client used for the archive XRPC calls (planSnapshot, getSegment, getBlock),
    /// the public dictionary fetch, and handshake-error classification. When null the client
    /// creates and owns its own.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Logger factory for diagnostics (reconnects, degraded compression, skipped frames).
    /// Null discards all output.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// Bearer API key for the archive endpoints. Sent as "Authorization: Bearer &lt;key&gt;" on
    /// planSnapshot, getSegment, and getBlock only — never on getZstdDictionary or the live
    /// websocket, which remain public. The value is a bearer secret: use TLS and keep it out
    /// of logs and process arguments.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Opts the live tail into the dictionary-zstd compression scheme: the client fetches the
    /// server's current dictionary via getZstdDictionary before the first dial, negotiates it
    /// with ?zstdDictionary=&lt;id&gt;, and transparently decompresses binary frames. Fetch or
    /// rotation-recovery failure degrades to an uncompressed tail (logged), never a stream
    /// failure. Default false.
    /// </summary>
    public bool EnableCompression { get; set; }

    /// <summary>
    /// Archive-replay parallelism: how many block frames are fetched and decoded concurrently.
    /// Defaults to the processor count clamped to [4, 32].
    /// </summary>
    public int DownloadConcurrency { get; set; } = DefaultConcurrency;

    /// <summary>
    /// Upper bound on a single live websocket message (and on its decompressed size when
    /// compression is on), guarding against unbounded allocations. Default 32 MiB — v2 frames
    /// embed whole records.
    /// </summary>
    public int ReadLimitBytes { get; set; } = 32 * 1024 * 1024;

    /// <summary>
    /// Total attempts (initial request plus retries) for each archive HTTP request before the
    /// failure is surfaced. Default 3; 1 disables retries.
    /// </summary>
    public int MaxDownloadAttempts { get; set; } = 3;

    /// <summary>
    /// Live-tail reconnect backoff floor. Default 250 ms; the delay doubles per failed attempt
    /// up to <see cref="ReconnectBackoffMax"/> and resets when a session delivers events.
    /// </summary>
    public TimeSpan ReconnectBackoffMin { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Live-tail reconnect backoff ceiling. Default 30 seconds.
    /// </summary>
    public TimeSpan ReconnectBackoffMax { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (DownloadConcurrency < 1)
        {
            throw new ArgumentException($"{nameof(DownloadConcurrency)} must be at least 1.");
        }

        if (ReadLimitBytes < 1024)
        {
            throw new ArgumentException($"{nameof(ReadLimitBytes)} must be at least 1024.");
        }

        if (MaxDownloadAttempts < 1)
        {
            throw new ArgumentException($"{nameof(MaxDownloadAttempts)} must be at least 1.");
        }

        if (ReconnectBackoffMin <= TimeSpan.Zero || ReconnectBackoffMax < ReconnectBackoffMin)
        {
            throw new ArgumentException(
                $"{nameof(ReconnectBackoffMin)} must be positive and no greater than {nameof(ReconnectBackoffMax)}.");
        }
    }
}
