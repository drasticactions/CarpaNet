using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.Identity;

/// <summary>
/// DNS resolver that queries TXT records over HTTPS using the JSON API
/// (<c>application/dns-json</c>) served by public resolvers such as Cloudflare and Google.
/// </summary>
/// <remarks>
/// <para>
/// Use this resolver where raw UDP DNS is unavailable, such as in browsers (WebAssembly),
/// or on networks that block outbound UDP port 53.
/// </para>
/// <para>
/// Each endpoint is queried with
/// <c>GET {endpoint}?name={name}&amp;type=TXT</c> and <c>Accept: application/dns-json</c>.
/// Endpoints are tried in order. The resolver moves to the next endpoint when a request
/// fails, times out, returns a non-success HTTP status, returns malformed JSON, or returns a
/// DNS status other than NOERROR (0) or NXDOMAIN (3). NXDOMAIN and NOERROR are authoritative
/// answers and stop the fallback. When every endpoint fails, an empty list is returned,
/// which matches <see cref="DefaultDnsResolver"/>.
/// </para>
/// </remarks>
public sealed class DnsOverHttpsResolver : IDnsResolver
{
    private const int DnsStatusNoError = 0;
    private const int DnsStatusNxDomain = 3;
    private const int DnsTypeTxt = 16;

    private readonly HttpClient _httpClient;
    private readonly string[] _endpoints;
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Cloudflare DNS-over-HTTPS JSON endpoint.
    /// </summary>
    public const string CloudflareEndpoint = "https://cloudflare-dns.com/dns-query";

    /// <summary>
    /// Google Public DNS JSON endpoint.
    /// </summary>
    public const string GoogleEndpoint = "https://dns.google/resolve";

    /// <summary>
    /// Default endpoints, tried in order (Cloudflare, then Google).
    /// </summary>
    public static IReadOnlyList<string> DefaultEndpoints { get; } = new[] { CloudflareEndpoint, GoogleEndpoint };

    /// <summary>
    /// Default timeout for each endpoint request.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Creates a new DnsOverHttpsResolver.
    /// </summary>
    /// <param name="httpClient">The HttpClient used for requests. The resolver does not dispose it.</param>
    /// <param name="endpoints">DNS JSON endpoints to query, in fallback order. If null or empty, <see cref="DefaultEndpoints"/> is used.</param>
    /// <param name="timeout">Timeout for each endpoint request. If null, <see cref="DefaultTimeout"/> is used.</param>
    public DnsOverHttpsResolver(HttpClient httpClient, IEnumerable<string>? endpoints = null, TimeSpan? timeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        var list = new List<string>();
        if (endpoints != null)
        {
            foreach (var endpoint in endpoints)
            {
                if (!string.IsNullOrWhiteSpace(endpoint))
                    list.Add(endpoint.Trim());
            }
        }

        _endpoints = list.Count > 0 ? list.ToArray() : new List<string>(DefaultEndpoints).ToArray();
        _timeout = timeout ?? DefaultTimeout;

        if (_timeout <= TimeSpan.Zero && _timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive or Timeout.InfiniteTimeSpan.");
    }

    /// <summary>
    /// Gets the configured endpoints, in fallback order.
    /// </summary>
    public IReadOnlyList<string> Endpoints => _endpoints;

    /// <inheritdoc/>
    /// <remarks>
    /// Multiple character-strings in one TXT record are joined into one string.
    /// Cancellation of <paramref name="cancellationToken"/> is propagated as an
    /// <see cref="OperationCanceledException"/>; per-endpoint timeouts are not.
    /// </remarks>
    public async Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));

        foreach (var endpoint in _endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var records = await TryQueryEndpointAsync(endpoint, name, cancellationToken).ConfigureAwait(false);
            if (records != null)
                return records;
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Queries one endpoint. Returns null when the next endpoint should be tried.
    /// </summary>
    private async Task<IReadOnlyList<string>?> TryQueryEndpointAsync(string endpoint, string name, CancellationToken cancellationToken)
    {
        var separator = endpoint.IndexOf('?') >= 0 ? "&" : "?";
        var url = $"{endpoint}{separator}name={Uri.EscapeDataString(name)}&type=TXT";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_timeout != Timeout.InfiniteTimeSpan)
            cts.CancelAfter(_timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "application/dns-json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

#if NET5_0_OR_GREATER
            using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
#else
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
            var dnsResponse = await JsonSerializer.DeserializeAsync(stream, IdentityJsonContext.Default.DnsJsonResponse, cts.Token).ConfigureAwait(false);
            if (dnsResponse == null)
                return null;

            return ParseResponse(dnsResponse);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Per-endpoint timeout; try next endpoint
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a DNS JSON response to TXT strings. Returns null when the status is not authoritative.
    /// </summary>
    internal static IReadOnlyList<string>? ParseResponse(DnsJsonResponse response)
    {
        if (response.Status == DnsStatusNxDomain)
            return Array.Empty<string>();

        if (response.Status != DnsStatusNoError)
            return null;

        var results = new List<string>();
        if (response.Answer == null)
            return results;

        foreach (var answer in response.Answer)
        {
            if (answer == null || answer.Type != DnsTypeTxt || answer.Data == null)
                continue;

            results.Add(ParseTxtData(answer.Data));
        }

        return results;
    }

    /// <summary>
    /// Parses TXT presentation data. Quoted character-strings are unquoted, unescaped, and joined.
    /// Unquoted data is returned trimmed.
    /// </summary>
    /// <param name="data">The <c>data</c> value of a TXT answer, for example <c>"\"did=did:plc:abc\""</c>.</param>
    /// <returns>The TXT record text.</returns>
    public static string ParseTxtData(string data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var trimmed = data.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '"')
            return trimmed;

        var builder = new StringBuilder(trimmed.Length);
        var i = 0;
        while (i < trimmed.Length)
        {
            // Skip whitespace between character-strings
            while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i]))
                i++;

            if (i >= trimmed.Length)
                break;

            if (trimmed[i] != '"')
            {
                // Unquoted segment: read up to the next whitespace
                var start = i;
                while (i < trimmed.Length && !char.IsWhiteSpace(trimmed[i]))
                    i++;
                builder.Append(trimmed, start, i - start);
                continue;
            }

            i++; // Opening quote
            while (i < trimmed.Length && trimmed[i] != '"')
            {
                var c = trimmed[i];
                if (c == '\\' && i + 1 < trimmed.Length)
                {
                    // \DDD decimal escape
                    if (i + 3 < trimmed.Length
                        && char.IsDigit(trimmed[i + 1]) && char.IsDigit(trimmed[i + 2]) && char.IsDigit(trimmed[i + 3]))
                    {
                        var value = ((trimmed[i + 1] - '0') * 100) + ((trimmed[i + 2] - '0') * 10) + (trimmed[i + 3] - '0');
                        builder.Append((char)value);
                        i += 4;
                        continue;
                    }

                    builder.Append(trimmed[i + 1]);
                    i += 2;
                    continue;
                }

                builder.Append(c);
                i++;
            }

            i++; // Closing quote
        }

        return builder.ToString();
    }
}

/// <summary>
/// DNS JSON API response (<c>application/dns-json</c>).
/// </summary>
internal sealed class DnsJsonResponse
{
    /// <summary>
    /// DNS response code (0 = NOERROR, 2 = SERVFAIL, 3 = NXDOMAIN).
    /// </summary>
    [JsonPropertyName("Status")]
    public int Status { get; set; }

    /// <summary>
    /// Answer records.
    /// </summary>
    [JsonPropertyName("Answer")]
    public List<DnsJsonAnswer?>? Answer { get; set; }
}

/// <summary>
/// One answer record in a DNS JSON API response.
/// </summary>
internal sealed class DnsJsonAnswer
{
    /// <summary>
    /// Record owner name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Record type (16 = TXT).
    /// </summary>
    [JsonPropertyName("type")]
    public int Type { get; set; }

    /// <summary>
    /// Time to live in seconds.
    /// </summary>
    [JsonPropertyName("TTL")]
    public int Ttl { get; set; }

    /// <summary>
    /// Record data in presentation format.
    /// </summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }
}
