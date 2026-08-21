using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CarpaNet.Jetstream;

/// <summary>
/// HTTP transport for the Jetstream v2 archive XRPC endpoints (planSnapshot, getSegment,
/// getBlock) and the public dictionary fetch. Applies bearer authentication to the archive
/// endpoints only, retries transient failures with exponential backoff, and converts XRPC
/// error envelopes into <see cref="JetstreamV2Exception"/> values with structured error names.
/// </summary>
internal sealed class JetstreamV2Transport
{
    internal const string PlanSnapshotNsid = "network.bsky.jetstream.planSnapshot";
    internal const string GetSegmentNsid = "network.bsky.jetstream.getSegment";
    internal const string GetBlockNsid = "network.bsky.jetstream.getBlock";
    internal const string GetZstdDictionaryNsid = "network.bsky.jetstream.getZstdDictionary";
    internal const string SubscribeEventsNsid = "network.bsky.jetstream.subscribeEvents";

    // Bounds a single segment/block allocation: a corrupt or hostile Content-Length must not
    // make the client allocate unbounded memory. Sealed segments are ~256-280 MB.
    private const long MaxDownloadBytes = 1L << 30;

    private static readonly TimeSpan RetryBackoffBase = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryBackoffMax = TimeSpan.FromSeconds(5);

    private readonly Uri _baseUri;
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly int _maxAttempts;
    private readonly ILogger _logger;

    public JetstreamV2Transport(Uri baseUri, HttpClient httpClient, string? apiKey, int maxAttempts, ILogger logger)
    {
        _baseUri = baseUri;
        _httpClient = httpClient;
        _apiKey = apiKey;
        _maxAttempts = maxAttempts;
        _logger = logger;
    }

    /// <summary>
    /// Builds an absolute /xrpc/&lt;nsid&gt; URI with optional query parameters.
    /// </summary>
    internal static Uri BuildXrpcUri(Uri baseUri, string nsid, IEnumerable<KeyValuePair<string, string>>? query = null)
    {
        var builder = new UriBuilder(baseUri)
        {
            Path = "/xrpc/" + nsid,
        };

        if (query != null)
        {
            var parts = new List<string>();
            foreach (var pair in query)
            {
                parts.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
            }

            if (parts.Count > 0)
            {
                builder.Query = string.Join("&", parts);
            }
        }

        return builder.Uri;
    }

    public async Task<JetstreamV2PlanSnapshotOutput> PlanSnapshotAsync(JetstreamV2PlanSnapshotInput input, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(input, JetstreamV2JsonContext.Default.JetstreamV2PlanSnapshotInput);
        var body = await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, BuildXrpcUri(_baseUri, PlanSnapshotNsid))
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                return request;
            },
            authenticated: true,
            cancellationToken).ConfigureAwait(false);

        JetstreamV2PlanSnapshotOutput? output;
        try
        {
            output = JsonSerializer.Deserialize(body, JetstreamV2JsonContext.Default.JetstreamV2PlanSnapshotOutput);
        }
        catch (JsonException ex)
        {
            throw new JetstreamV2Exception($"planSnapshot returned invalid JSON: {ex.Message}", ex);
        }

        if (output == null)
        {
            throw new JetstreamV2Exception("planSnapshot returned an empty response");
        }

        return output;
    }

    public Task<byte[]> GetSegmentAsync(string name, string? expectedChecksum, CancellationToken cancellationToken)
    {
        return SendAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildXrpcUri(_baseUri, GetSegmentNsid, new[] { new KeyValuePair<string, string>("name", name) })),
            authenticated: true,
            cancellationToken,
            expectedChecksum);
    }

    public Task<byte[]> GetBlockAsync(string segment, long blockIndex, CancellationToken cancellationToken)
    {
        return SendAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildXrpcUri(_baseUri, GetBlockNsid, new[]
                {
                    new KeyValuePair<string, string>("segment", segment),
                    new KeyValuePair<string, string>("blockIndex", blockIndex.ToString(CultureInfo.InvariantCulture)),
                })),
            authenticated: true,
            cancellationToken);
    }

    public Task<byte[]> GetZstdDictionaryAsync(long? id, CancellationToken cancellationToken)
    {
        var query = id == null
            ? null
            : new[] { new KeyValuePair<string, string>("id", id.Value.ToString(CultureInfo.InvariantCulture)) };

        // The dictionary is public: the archive credential is deliberately never sent here.
        return SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildXrpcUri(_baseUri, GetZstdDictionaryNsid, query)),
            authenticated: false,
            cancellationToken);
    }

    /// <summary>
    /// Classifies a failed websocket handshake by re-issuing the subscribe request as a plain
    /// HTTP GET: the server validates parameters before upgrading, so an invalid cursor,
    /// dictionary, or filter yields the same pre-upgrade XRPC error envelope. Returns null when
    /// the failure cannot be classified (treated as transient by the caller).
    /// </summary>
    public async Task<JetstreamV2XrpcError?> ProbeHandshakeErrorAsync(Uri subscribeHttpUri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, subscribeHttpUri);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.BadRequest &&
                response.StatusCode != HttpStatusCode.ServiceUnavailable)
            {
                return null;
            }

            var body = await ReadBoundedAsync(response, 4096, cancellationToken).ConfigureAwait(false);
            return ParseErrorEnvelope(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<byte[]> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        bool authenticated,
        CancellationToken cancellationToken,
        string? expectedChecksum = null)
    {
        var backoff = RetryBackoffBase;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Exception? transient = null;
            try
            {
                using var request = requestFactory();
                if (authenticated && !string.IsNullOrEmpty(_apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                }

                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    VerifyChecksumHeader(response, expectedChecksum, request.RequestUri);
                    return await ReadBoundedAsync(response, MaxDownloadBytes, cancellationToken).ConfigureAwait(false);
                }

                var status = (int)response.StatusCode;
                var errorBody = await ReadBoundedAsync(response, 4096, cancellationToken).ConfigureAwait(false);
                var envelope = ParseErrorEnvelope(errorBody);
                if (status >= 500 || status == 429 || envelope?.Error == JetstreamV2ErrorNames.ServiceUnavailable)
                {
                    transient = new JetstreamV2Exception(
                        $"HTTP {status} from {request.RequestUri}: {envelope?.Error ?? Truncate(errorBody)}",
                        envelope?.Error);
                }
                else
                {
                    throw new JetstreamV2Exception(
                        envelope?.Error != null
                            ? $"{envelope.Error}: {envelope.Message ?? request.RequestUri!.ToString()}"
                            : $"HTTP {status} from {request.RequestUri}: {Truncate(errorBody)}",
                        envelope?.Error);
                }
            }
            catch (HttpRequestException ex)
            {
                transient = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient timeout, not a caller cancellation.
                transient = ex;
            }

            if (attempt >= _maxAttempts)
            {
                throw transient as JetstreamV2Exception
                    ?? new JetstreamV2Exception($"request failed after {attempt} attempts: {transient!.Message}", transient);
            }

            _logger.LogWarning("Jetstream request failed (attempt {Attempt}/{Max}): {Error}; retrying in {Delay}", attempt, _maxAttempts, transient!.Message, backoff);
            await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            backoff = backoff.TotalMilliseconds * 2 > RetryBackoffMax.TotalMilliseconds
                ? RetryBackoffMax
                : TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * 2);
        }
    }

    /// <summary>
    /// A downloaded segment's ETag equals its plan checksum (the segment-generation pin).
    /// A mismatch means a compaction rewrote the file between planning and download; the rows
    /// are still valid, so log rather than fail.
    /// </summary>
    private void VerifyChecksumHeader(HttpResponseMessage response, string? expectedChecksum, Uri? requestUri)
    {
        if (string.IsNullOrEmpty(expectedChecksum))
        {
            return;
        }

        var etag = response.Headers.ETag?.Tag;
        if (etag != null && etag.IndexOf(expectedChecksum!, StringComparison.OrdinalIgnoreCase) < 0)
        {
            _logger.LogWarning(
                "Segment generation changed between plan and download ({Uri}): planned checksum {Checksum}, got ETag {ETag}",
                requestUri, expectedChecksum, etag);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, long limit, CancellationToken cancellationToken)
    {
        var contentLength = response.Content?.Headers.ContentLength;
        if (contentLength > limit)
        {
            throw new JetstreamV2Exception($"response of {contentLength} bytes exceeds the {limit} byte cap");
        }

        if (response.Content == null)
        {
            return Array.Empty<byte>();
        }

        using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var buffer = contentLength is > 0 and <= int.MaxValue
            ? new MemoryStream((int)contentLength.Value)
            : new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                throw new JetstreamV2Exception($"response exceeded the {limit} byte cap");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static JetstreamV2XrpcError? ParseErrorEnvelope(byte[] body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize(body, JetstreamV2JsonContext.Default.JetstreamV2XrpcError);
            return string.IsNullOrEmpty(envelope?.Error) ? null : envelope;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(byte[] body)
    {
        var text = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 256));
        return text.Replace('\n', ' ').Replace('\r', ' ');
    }
}
