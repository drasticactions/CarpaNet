using System;
using System.Collections.Generic;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace CarpaNet.BuildTasks;

/// <summary>
/// MSBuild task that resolves AT Protocol handles to DIDs, then enumerates their lexicon records.
/// Invoked when <see cref="LexiconResolveHandle"/> items are present.
/// </summary>
public sealed class ResolveHandleLexiconsTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Handles to resolve (from LexiconResolveHandle MSBuild items).
    /// </summary>
    [Required]
    public ITaskItem[] Handles { get; set; } = Array.Empty<ITaskItem>();

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
        if (Handles.Length == 0)
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

        foreach (var item in Handles)
        {
            var handle = item.ItemSpec;

            // Strip leading @ (common copy-paste from Bluesky)
            if (handle.StartsWith("@", StringComparison.Ordinal))
                handle = handle.Substring(1);

            if (!IsValidHandle(handle))
            {
                Log.LogError("Invalid handle format: '{0}'. Handles must have at least 2 dot-separated segments with a valid TLD (last segment must not start with a digit).", handle);
                return false;
            }

            try
            {
                // Check authority manifest cache first (keyed by handle)
                var cachedNsids = cache.TryGetAuthorityManifest(handle);
                if (cachedNsids != null)
                {
                    Log.LogMessage(MessageImportance.Low, "Using cached handle manifest for '{0}' ({1} lexicons)", handle, cachedNsids.Count);

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

                    Log.LogMessage(MessageImportance.Normal, "Some cached lexicons for handle '{0}' are missing, re-resolving", handle);
                }

                // Resolve from network: handle → DID → PDS → listRecords
                var resolved = resolver.ResolveHandleAsync(handle).GetAwaiter().GetResult();
                var nsidList = new List<string>();

                foreach (var (nsid, json) in resolved)
                {
                    cache.Store(nsid, json);
                    resolvedFiles.Add(new TaskItem(cache.GetJsonPath(nsid)));
                    nsidList.Add(nsid);
                    Log.LogMessage(MessageImportance.Normal, "Resolved lexicon: {0}", nsid);
                }

                cache.StoreAuthorityManifest(handle, nsidList);
                Log.LogMessage(MessageImportance.Normal, "Resolved {0} lexicons for handle '{1}'", nsidList.Count, handle);
            }
            catch (LexiconResolutionException ex)
            {
                if (FailOnError)
                {
                    Log.LogError("Handle lexicon resolution failed for '{0}': {1}", handle, ex.Message);
                    return false;
                }
                else
                {
                    Log.LogWarning("Handle lexicon resolution failed for '{0}' (continuing because CarpaNet_LexiconFailOnError=false): {1}", handle, ex.Message);
                }
            }
            catch (Exception ex)
            {
                if (FailOnError)
                {
                    Log.LogError("Handle lexicon resolution failed for '{0}' with unexpected error: {1}", handle, ex.Message);
                    return false;
                }
                else
                {
                    Log.LogWarning("Handle lexicon resolution failed for '{0}' with unexpected error (continuing because CarpaNet_LexiconFailOnError=false): {1}", handle, ex.Message);
                }
            }
        }

        ResolvedLexiconFiles = resolvedFiles.ToArray();
        return !Log.HasLoggedErrors;
    }

    /// <summary>
    /// Validates that a string is a well-formed AT Protocol handle.
    /// Handles are domain-like: at least 2 dot-separated segments, each alphanumeric/hyphen,
    /// and the last segment (TLD) must not start with a digit.
    /// </summary>
    internal static bool IsValidHandle(string handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
            return false;

        var segments = handle.Split('.');
        if (segments.Length < 2)
            return false;

        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment) || segment.Length > 63)
                return false;

            if (!char.IsLetterOrDigit(segment[0]) || !char.IsLetterOrDigit(segment[segment.Length - 1]))
                return false;

            foreach (var c in segment)
            {
                if (!char.IsLetterOrDigit(c) && c != '-')
                    return false;
            }
        }

        // Last segment (TLD) must not start with a digit
        if (char.IsDigit(segments[segments.Length - 1][0]))
            return false;

        return true;
    }
}
