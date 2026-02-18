using System;
using System.Collections.Generic;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace CarpaNet.BuildTasks;

/// <summary>
/// MSBuild task that enumerates all lexicon records published by an authority
/// using com.atproto.repo.listRecords. Invoked when <see cref="LexiconResolveAuthority"/> items are present.
/// </summary>
public sealed class ResolveAuthorityLexiconsTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Authorities to resolve (from LexiconResolveAuthority MSBuild items).
    /// </summary>
    [Required]
    public ITaskItem[] Authorities { get; set; } = Array.Empty<ITaskItem>();

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
    /// Output: resolved lexicon file paths to be fed into LexiconFiles.
    /// </summary>
    [Output]
    public ITaskItem[] ResolvedLexiconFiles { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        if (Authorities.Length == 0)
            return true;

        var cache = new LexiconCache(CacheDir, CacheTtlHours);
        var resolvedFiles = new List<ITaskItem>();

        var dnsServers = string.IsNullOrWhiteSpace(DnsServers)
            ? null
            : DnsServers.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        using var resolver = new LexiconResolver(
            PlcDirectoryUrl,
            dnsServers,
            msg => Log.LogMessage(MessageImportance.Normal, msg),
            msg => Log.LogWarning(msg));

        foreach (var item in Authorities)
        {
            var authority = item.ItemSpec;

            if (!NsidAuthority.IsValidAuthority(authority))
            {
                Log.LogError("Invalid authority format: '{0}'. Authorities must have at least 2 dot-separated segments.", authority);
                return false;
            }

            try
            {
                // Check authority manifest cache first
                var cachedNsids = cache.TryGetAuthorityManifest(authority);
                if (cachedNsids != null)
                {
                    Log.LogMessage(MessageImportance.Low, "Using cached authority manifest for '{0}' ({1} lexicons)", authority, cachedNsids.Count);

                    // Verify all individual NSID caches still exist
                    var allCached = true;
                    foreach (var nsid in cachedNsids)
                    {
                        if (!cache.IsCached(nsid))
                        {
                            allCached = false;
                            break;
                        }
                    }

                    if (allCached)
                    {
                        foreach (var nsid in cachedNsids)
                        {
                            resolvedFiles.Add(new TaskItem(cache.GetJsonPath(nsid)));
                        }

                        continue;
                    }

                    Log.LogMessage(MessageImportance.Normal, "Some cached lexicons for authority '{0}' are missing, re-resolving", authority);
                }

                // Resolve from network
                var resolved = resolver.ResolveAuthorityAsync(authority).GetAwaiter().GetResult();
                var nsidList = new List<string>();

                foreach (var (nsid, json) in resolved)
                {
                    cache.Store(nsid, json);
                    resolvedFiles.Add(new TaskItem(cache.GetJsonPath(nsid)));
                    nsidList.Add(nsid);
                    Log.LogMessage(MessageImportance.Normal, "Resolved lexicon: {0}", nsid);
                }

                cache.StoreAuthorityManifest(authority, nsidList);
                Log.LogMessage(MessageImportance.Normal, "Resolved {0} lexicons for authority '{1}'", nsidList.Count, authority);
            }
            catch (LexiconResolutionException ex)
            {
                if (FailOnError)
                {
                    Log.LogError("Authority lexicon resolution failed for '{0}': {1}", authority, ex.Message);
                    return false;
                }
                else
                {
                    Log.LogWarning("Authority lexicon resolution failed for '{0}' (continuing because CarpaNet_LexiconFailOnError=false): {1}", authority, ex.Message);
                }
            }
            catch (Exception ex)
            {
                if (FailOnError)
                {
                    Log.LogError("Authority lexicon resolution failed for '{0}' with unexpected error: {1}", authority, ex.Message);
                    return false;
                }
                else
                {
                    Log.LogWarning("Authority lexicon resolution failed for '{0}' with unexpected error (continuing because CarpaNet_LexiconFailOnError=false): {1}", authority, ex.Message);
                }
            }
        }

        ResolvedLexiconFiles = resolvedFiles.ToArray();
        return !Log.HasLoggedErrors;
    }
}
