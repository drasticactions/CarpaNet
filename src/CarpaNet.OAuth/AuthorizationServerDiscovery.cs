using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.OAuth;

/// <summary>
/// Discovers and caches OAuth authorization server metadata.
/// </summary>
public sealed class AuthorizationServerDiscovery : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<string, CachedMetadata> _cache = new();
    private bool _disposed;

    private sealed class CachedMetadata
    {
        public OAuthAuthorizationServerMetadata Metadata { get; set; } = null!;
        public DateTimeOffset ExpiresAt { get; set; }
    }

    /// <summary>
    /// Creates a new authorization server discovery client.
    /// </summary>
    /// <param name="cacheTtl">Cache time-to-live (default: 60 seconds).</param>
    public AuthorizationServerDiscovery(TimeSpan? cacheTtl = null)
        : this(new HttpClient(), ownsHttpClient: true, cacheTtl)
    {
    }

    /// <summary>
    /// Creates a new authorization server discovery client with a custom HttpClient.
    /// </summary>
    /// <param name="httpClient">The HttpClient to use.</param>
    /// <param name="cacheTtl">Cache time-to-live (default: 60 seconds).</param>
    public AuthorizationServerDiscovery(HttpClient httpClient, TimeSpan? cacheTtl = null)
        : this(httpClient, ownsHttpClient: false, cacheTtl)
    {
    }

    private AuthorizationServerDiscovery(HttpClient httpClient, bool ownsHttpClient, TimeSpan? cacheTtl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
        _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Gets the well-known URL for OAuth authorization server metadata.
    /// </summary>
    /// <param name="issuer">The issuer URL.</param>
    /// <returns>The well-known metadata URL.</returns>
    public static string GetWellKnownUrl(string issuer)
    {
        var uri = new Uri(issuer.TrimEnd('/'));
        return $"{uri.Scheme}://{uri.Authority}/.well-known/oauth-authorization-server";
    }

    /// <summary>
    /// Gets the well-known URL for OAuth protected resource metadata (PDS).
    /// </summary>
    /// <param name="resourceUrl">The resource server URL.</param>
    /// <returns>The well-known metadata URL.</returns>
    public static string GetProtectedResourceWellKnownUrl(string resourceUrl)
    {
        var uri = new Uri(resourceUrl.TrimEnd('/'));
        return $"{uri.Scheme}://{uri.Authority}/.well-known/oauth-protected-resource";
    }

    /// <summary>
    /// Fetches authorization server metadata from the issuer.
    /// </summary>
    /// <param name="issuer">The authorization server issuer URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization server metadata.</returns>
    public async Task<OAuthAuthorizationServerMetadata> GetMetadataAsync(
        string issuer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Check cache
        if (_cache.TryGetValue(issuer, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Metadata;
        }

        // Fetch metadata
        var wellKnownUrl = GetWellKnownUrl(issuer);
        var response = await _httpClient.GetAsync(wellKnownUrl, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new OAuthException(
                "metadata_fetch_failed",
                $"Failed to fetch authorization server metadata from {wellKnownUrl}: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var metadata = JsonSerializer.Deserialize(content, OAuthJsonContext.Default.OAuthAuthorizationServerMetadata);

        if (metadata == null)
        {
            throw new OAuthException(
                "metadata_parse_failed",
                "Failed to parse authorization server metadata.");
        }

        // Validate issuer matches (mix-up attack prevention)
        if (!string.Equals(metadata.Issuer, issuer, StringComparison.OrdinalIgnoreCase))
        {
            throw new OAuthException(
                "issuer_mismatch",
                $"Authorization server metadata issuer '{metadata.Issuer}' does not match expected '{issuer}'.");
        }

        // Cache the result
        _cache[issuer] = new CachedMetadata
        {
            Metadata = metadata,
            ExpiresAt = DateTimeOffset.UtcNow + _cacheTtl
        };

        return metadata;
    }

    /// <summary>
    /// Fetches protected resource metadata from a PDS to discover its authorization server.
    /// </summary>
    /// <param name="resourceUrl">The PDS URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization server issuer URL.</returns>
    public async Task<string> DiscoverAuthorizationServerAsync(
        string resourceUrl,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var wellKnownUrl = GetProtectedResourceWellKnownUrl(resourceUrl);
        var response = await _httpClient.GetAsync(wellKnownUrl, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new OAuthException(
                "resource_metadata_fetch_failed",
                $"Failed to fetch protected resource metadata from {wellKnownUrl}: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(content);

        if (!doc.RootElement.TryGetProperty("authorization_servers", out var servers) ||
            servers.GetArrayLength() == 0)
        {
            throw new OAuthException(
                "no_authorization_server",
                "Protected resource does not specify any authorization servers.");
        }

        // Use the first authorization server
        return servers[0].GetString()
            ?? throw new OAuthException("invalid_authorization_server", "Authorization server URL is null.");
    }

    /// <summary>
    /// Clears the metadata cache.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AuthorizationServerDiscovery));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
