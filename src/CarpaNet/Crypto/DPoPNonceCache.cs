using System;
using System.Collections.Concurrent;

namespace CarpaNet.OAuth.Crypto;

/// <summary>
/// Caches DPoP nonces per origin.
/// </summary>
public sealed class DPoPNonceCache
{
    private readonly ConcurrentDictionary<string, NonceEntry> _cache = new();
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;

    private sealed class NonceEntry
    {
        public string Nonce { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
    }

    /// <summary>
    /// Creates a new DPoP nonce cache.
    /// </summary>
    /// <param name="ttl">Time-to-live for cached nonces (default: 60 seconds).</param>
    /// <param name="maxEntries">Maximum number of cached entries (default: 100).</param>
    public DPoPNonceCache(TimeSpan? ttl = null, int maxEntries = 100)
    {
        _ttl = ttl ?? TimeSpan.FromSeconds(60);
        _maxEntries = maxEntries;
    }

    /// <summary>
    /// Gets the origin (scheme + host) from a URL.
    /// </summary>
    public static string GetOrigin(string url)
    {
        var uri = new Uri(url);
        return $"{uri.Scheme}://{uri.Authority}";
    }

    /// <summary>
    /// Gets a cached nonce for the given URL's origin.
    /// </summary>
    /// <param name="url">The request URL.</param>
    /// <returns>The cached nonce, or null if not found or expired.</returns>
    public string? Get(string url)
    {
        var origin = GetOrigin(url);

        if (_cache.TryGetValue(origin, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return entry.Nonce;
        }

        return null;
    }

    /// <summary>
    /// Stores a nonce for the given URL's origin.
    /// </summary>
    /// <param name="url">The request URL.</param>
    /// <param name="nonce">The nonce to store.</param>
    public void Set(string url, string nonce)
    {
        var origin = GetOrigin(url);

        // Simple eviction: if we're over capacity, clear oldest entries
        if (_cache.Count >= _maxEntries)
        {
            // Clear expired entries first
            var now = DateTimeOffset.UtcNow;
            foreach (var key in _cache.Keys)
            {
                if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt <= now)
                {
                    _cache.TryRemove(key, out _);
                }
            }
        }

        _cache[origin] = new NonceEntry
        {
            Nonce = nonce,
            ExpiresAt = DateTimeOffset.UtcNow + _ttl
        };
    }

    /// <summary>
    /// Clears all cached nonces.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }
}
