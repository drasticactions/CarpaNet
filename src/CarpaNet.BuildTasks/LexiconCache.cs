using System;
using System.Globalization;
using System.IO;

namespace CarpaNet.BuildTasks;

/// <summary>
/// Disk-based cache for resolved lexicon JSON files.
/// Each NSID is stored as {nsid}.json with a companion {nsid}.meta file containing the timestamp.
/// </summary>
internal sealed class LexiconCache
{
    private readonly string _cacheDir;
    private readonly TimeSpan _ttl;

    public LexiconCache(string cacheDir, double ttlHours = 24)
    {
        _cacheDir = cacheDir;
        _ttl = TimeSpan.FromHours(ttlHours);
    }

    /// <summary>
    /// Gets the cache directory path.
    /// </summary>
    public string CacheDirectory => _cacheDir;

    /// <summary>
    /// Tries to retrieve a cached lexicon for the given NSID.
    /// Returns null if not cached or expired.
    /// </summary>
    public string? TryGet(string nsid)
    {
        var jsonPath = GetJsonPath(nsid);
        var metaPath = GetMetaPath(nsid);

        if (!File.Exists(jsonPath) || !File.Exists(metaPath))
            return null;

        // Check TTL
        var metaContent = File.ReadAllText(metaPath).Trim();
        if (!DateTimeOffset.TryParse(metaContent, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cachedAt))
            return null;

        if (DateTimeOffset.UtcNow - cachedAt > _ttl)
            return null;

        return File.ReadAllText(jsonPath);
    }

    /// <summary>
    /// Stores a lexicon JSON in the cache.
    /// </summary>
    public void Store(string nsid, string lexiconJson)
    {
        Directory.CreateDirectory(_cacheDir);

        var jsonPath = GetJsonPath(nsid);
        var metaPath = GetMetaPath(nsid);

        File.WriteAllText(jsonPath, lexiconJson);
        File.WriteAllText(metaPath, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Gets the path where the cached JSON file would be stored for the given NSID.
    /// </summary>
    public string GetJsonPath(string nsid) => Path.Combine(_cacheDir, nsid + ".json");

    /// <summary>
    /// Gets the path of the metadata file for the given NSID.
    /// </summary>
    public string GetMetaPath(string nsid) => Path.Combine(_cacheDir, nsid + ".meta");

    /// <summary>
    /// Checks whether a valid (non-expired) cache entry exists for the given NSID.
    /// </summary>
    public bool IsCached(string nsid) => TryGet(nsid) != null;
}
