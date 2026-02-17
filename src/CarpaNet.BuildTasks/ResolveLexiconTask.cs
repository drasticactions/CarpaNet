using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace CarpaNet.BuildTasks;

/// <summary>
/// MSBuild task that resolves ATProtocol lexicon schemas from the network via DNS.
/// Invoked automatically when <see cref="LexiconResolve"/> items are present.
/// </summary>
public sealed class ResolveLexiconTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// NSIDs to resolve (from LexiconResolve MSBuild items).
    /// </summary>
    [Required]
    public ITaskItem[] Nsids { get; set; } = Array.Empty<ITaskItem>();

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
        if (Nsids.Length == 0)
            return true;

        var cache = new LexiconCache(CacheDir, CacheTtlHours);
        var resolvedFiles = new List<ITaskItem>();
        var nsidsToResolve = new List<string>();

        // Check cache first
        foreach (var item in Nsids)
        {
            var nsid = item.ItemSpec;

            if (!NsidAuthority.IsValidNsid(nsid))
            {
                Log.LogError("Invalid NSID format: '{0}'. NSIDs must have at least 3 dot-separated segments.", nsid);
                return false;
            }

            var cached = cache.TryGet(nsid);
            if (cached != null)
            {
                Log.LogMessage(MessageImportance.Low, "Using cached lexicon for '{0}'", nsid);
                resolvedFiles.Add(new TaskItem(cache.GetJsonPath(nsid)));
            }
            else
            {
                nsidsToResolve.Add(nsid);
            }
        }

        // Resolve any uncached NSIDs
        if (nsidsToResolve.Count > 0)
        {
            var dnsServers = string.IsNullOrWhiteSpace(DnsServers)
                ? null
                : DnsServers.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            using var resolver = new LexiconResolver(
                PlcDirectoryUrl,
                dnsServers,
                msg => Log.LogMessage(MessageImportance.Normal, msg),
                msg => Log.LogWarning(msg));

            try
            {
                var resolved = resolver.ResolveMultipleAsync(nsidsToResolve).GetAwaiter().GetResult();

                foreach (var (nsid, json) in resolved)
                {
                    cache.Store(nsid, json);
                    resolvedFiles.Add(new TaskItem(cache.GetJsonPath(nsid)));
                    Log.LogMessage(MessageImportance.Normal, "Resolved lexicon: {0}", nsid);
                }
            }
            catch (LexiconResolutionException ex)
            {
                if (FailOnError)
                {
                    Log.LogError("Lexicon resolution failed: {0}", ex.Message);
                    return false;
                }
                else
                {
                    Log.LogWarning("Lexicon resolution failed (continuing because CarpaNet_LexiconFailOnError=false): {0}", ex.Message);
                }
            }
            catch (Exception ex)
            {
                if (FailOnError)
                {
                    Log.LogError("Lexicon resolution failed with unexpected error: {0}", ex.Message);
                    return false;
                }
                else
                {
                    Log.LogWarning("Lexicon resolution failed with unexpected error (continuing because CarpaNet_LexiconFailOnError=false): {0}", ex.Message);
                }
            }
        }

        ResolvedLexiconFiles = resolvedFiles.ToArray();
        return !Log.HasLoggedErrors;
    }
}
