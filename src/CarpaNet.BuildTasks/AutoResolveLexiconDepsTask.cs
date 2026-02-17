// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace CarpaNet.BuildTasks;

/// <summary>
/// MSBuild task that iteratively discovers and resolves transitive lexicon dependencies.
/// Scans all known lexicon files for external NSID references, resolves any that are missing,
/// and repeats until no new dependencies are found (or MaxDepth is reached).
/// </summary>
public sealed class AutoResolveLexiconDepsTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// All currently known lexicon file paths (local + already resolved).
    /// </summary>
    [Required]
    public ITaskItem[] LexiconFiles { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// Directory to cache resolved lexicon files.
    /// </summary>
    [Required]
    public string CacheDir { get; set; } = string.Empty;

    /// <summary>
    /// Cache TTL in hours. Set to 0 to force refresh.
    /// </summary>
    public double CacheTtlHours { get; set; } = 24;

    /// <summary>
    /// Whether to fail the build on resolution errors (true) or just warn (false).
    /// </summary>
    public bool FailOnError { get; set; } = true;

    /// <summary>
    /// PLC directory URL for did:plc resolution.
    /// </summary>
    public string PlcDirectoryUrl { get; set; } = "https://plc.directory";

    /// <summary>
    /// Semicolon-separated DNS server IP addresses.
    /// </summary>
    public string DnsServers { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of resolution iterations (to guard against unbounded recursion).
    /// </summary>
    public int MaxDepth { get; set; } = 10;

    /// <summary>
    /// Output: additional resolved lexicon file paths discovered through transitive dependencies.
    /// </summary>
    [Output]
    public ITaskItem[] AdditionalResolvedFiles { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        if (LexiconFiles.Length == 0)
            return true;

        var cache = new LexiconCache(CacheDir, CacheTtlHours);
        var knownNsids = new HashSet<string>(StringComparer.Ordinal);
        var additionalFiles = new List<ITaskItem>();

        // Build initial known set from input files
        var filesToScan = new List<string>();
        foreach (var item in LexiconFiles)
        {
            var path = item.ItemSpec;
            if (!File.Exists(path))
                continue;

            var nsid = LexiconRefExtractor.ExtractNsidFromFile(path);
            if (nsid != null)
            {
                knownNsids.Add(nsid);
            }

            filesToScan.Add(path);
        }

        LexiconResolver? resolver = null;
        try
        {
            for (int iteration = 0; iteration < MaxDepth; iteration++)
            {
                if (filesToScan.Count == 0)
                    break;

                // Scan queued files for external refs
                var allRefs = new HashSet<string>(StringComparer.Ordinal);
                foreach (var path in filesToScan)
                {
                    try
                    {
                        var refs = LexiconRefExtractor.ExtractReferencedNsidsFromFile(path);
                        foreach (var r in refs)
                        {
                            allRefs.Add(r);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.LogMessage(MessageImportance.Low, "Failed to extract refs from '{0}': {1}", path, ex.Message);
                    }
                }

                // Compute missing
                var missing = new HashSet<string>(allRefs, StringComparer.Ordinal);
                missing.ExceptWith(knownNsids);

                if (missing.Count == 0)
                {
                    Log.LogMessage(MessageImportance.Normal,
                        "Auto-resolve: all transitive dependencies satisfied after {0} iteration(s).", iteration + 1);
                    break;
                }

                Log.LogMessage(MessageImportance.Normal,
                    "Auto-resolve iteration {0}: resolving {1} transitive dependency(ies): {2}",
                    iteration + 1, missing.Count, string.Join(", ", missing));

                // Check cache first, collect truly uncached
                var uncached = new List<string>();
                filesToScan = new List<string>();

                foreach (var nsid in missing)
                {
                    var cached = cache.TryGet(nsid);
                    if (cached != null)
                    {
                        var cachedPath = cache.GetJsonPath(nsid);
                        Log.LogMessage(MessageImportance.Low, "Using cached lexicon for transitive dep '{0}'", nsid);
                        additionalFiles.Add(new TaskItem(cachedPath));
                        knownNsids.Add(nsid);
                        filesToScan.Add(cachedPath);
                    }
                    else
                    {
                        uncached.Add(nsid);
                    }
                }

                // Resolve uncached NSIDs via network
                if (uncached.Count > 0)
                {
                    // Lazy resolver creation
                    if (resolver == null)
                    {
                        var dnsServers = string.IsNullOrWhiteSpace(DnsServers)
                            ? null
                            : DnsServers.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        resolver = new LexiconResolver(
                            PlcDirectoryUrl,
                            dnsServers,
                            msg => Log.LogMessage(MessageImportance.Normal, msg),
                            msg => Log.LogWarning(msg));
                    }

                    try
                    {
                        var resolved = resolver.ResolveMultipleAsync(uncached).GetAwaiter().GetResult();
                        foreach (var (nsid, json) in resolved)
                        {
                            cache.Store(nsid, json);
                            var resolvedPath = cache.GetJsonPath(nsid);
                            additionalFiles.Add(new TaskItem(resolvedPath));
                            knownNsids.Add(nsid);
                            filesToScan.Add(resolvedPath);
                            Log.LogMessage(MessageImportance.Normal, "Auto-resolved transitive dep: {0}", nsid);
                        }
                    }
                    catch (LexiconResolutionException ex)
                    {
                        if (FailOnError)
                        {
                            Log.LogError("Auto-resolve failed: {0}", ex.Message);
                            return false;
                        }

                        Log.LogWarning("Auto-resolve failed (continuing): {0}", ex.Message);
                    }
                    catch (Exception ex)
                    {
                        if (FailOnError)
                        {
                            Log.LogError("Auto-resolve failed with unexpected error: {0}", ex.Message);
                            return false;
                        }

                        Log.LogWarning("Auto-resolve failed with unexpected error (continuing): {0}", ex.Message);
                    }
                }
            }
        }
        finally
        {
            resolver?.Dispose();
        }

        AdditionalResolvedFiles = additionalFiles.ToArray();
        return !Log.HasLoggedErrors;
    }
}
