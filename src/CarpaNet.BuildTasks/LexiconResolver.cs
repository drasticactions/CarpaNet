using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.BuildTasks;

/// <summary>
/// Resolves ATProtocol lexicon schemas from the network.
/// Pipeline: DNS TXT lookup → DID resolution → PDS URL → fetch XRPC record.
/// </summary>
internal sealed class LexiconResolver : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly LexiconDnsResolver _dnsResolver;
    private readonly string _plcDirectoryUrl;
    private readonly Action<string> _logInfo;
    private readonly Action<string> _logWarning;

    public LexiconResolver(
        string plcDirectoryUrl = "https://plc.directory",
        string[]? dnsServers = null,
        Action<string>? logInfo = null,
        Action<string>? logWarning = null)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _dnsResolver = new LexiconDnsResolver(dnsServers);
        _plcDirectoryUrl = plcDirectoryUrl.TrimEnd('/');
        _logInfo = logInfo ?? (_ => { });
        _logWarning = logWarning ?? (_ => { });
    }

    /// <summary>
    /// Resolves a single NSID to its lexicon JSON content.
    /// </summary>
    public async Task<string> ResolveNsidAsync(string nsid, CancellationToken cancellationToken = default)
    {
        // Step 1: DNS TXT lookup for the authority
        var dnsName = NsidAuthority.NsidToDnsName(nsid);
        _logInfo($"Looking up DNS TXT record: {dnsName}");

        var did = await ResolveDnsToDidAsync(dnsName, cancellationToken).ConfigureAwait(false);
        if (did == null)
            throw new LexiconResolutionException($"No DID found in DNS TXT record for '{dnsName}' (NSID: {nsid})");

        _logInfo($"Resolved DID: {did}");

        // Step 2: Resolve DID to PDS endpoint
        var pdsUrl = await ResolveDidToPdsAsync(did, cancellationToken).ConfigureAwait(false);
        if (pdsUrl == null)
            throw new LexiconResolutionException($"No PDS endpoint found for DID '{did}' (NSID: {nsid})");

        _logInfo($"Resolved PDS: {pdsUrl}");

        // Step 3: Fetch the lexicon record from the PDS
        var lexiconJson = await FetchLexiconRecordAsync(pdsUrl, did, nsid, cancellationToken).ConfigureAwait(false);

        return lexiconJson;
    }

    /// <summary>
    /// Resolves multiple NSIDs, grouping by authority to minimize DNS lookups.
    /// Returns a dictionary of NSID → lexicon JSON.
    /// </summary>
    public async Task<Dictionary<string, string>> ResolveMultipleAsync(
        IEnumerable<string> nsids,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, string>();

        // Group by authority to share DNS + DID resolution
        var groups = nsids.GroupBy(NsidAuthority.GetAuthority);

        foreach (var group in groups)
        {
            var authority = group.Key;
            var dnsName = NsidAuthority.AuthorityToDnsName(authority);
            _logInfo($"Resolving authority '{authority}' via DNS: {dnsName}");

            string? did;
            string? pdsUrl;

            try
            {
                did = await ResolveDnsToDidAsync(dnsName, cancellationToken).ConfigureAwait(false);
                if (did == null)
                    throw new LexiconResolutionException($"No DID found in DNS TXT record for '{dnsName}'");

                pdsUrl = await ResolveDidToPdsAsync(did, cancellationToken).ConfigureAwait(false);
                if (pdsUrl == null)
                    throw new LexiconResolutionException($"No PDS endpoint found for DID '{did}'");
            }
            catch (Exception ex)
            {
                // Re-throw with all affected NSIDs
                var affectedNsids = string.Join(", ", group);
                throw new LexiconResolutionException(
                    $"Failed to resolve authority '{authority}' for NSIDs: {affectedNsids}. {ex.Message}", ex);
            }

            foreach (var nsid in group)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var json = await FetchLexiconRecordAsync(pdsUrl, did, nsid, cancellationToken).ConfigureAwait(false);
                    results[nsid] = json;
                    _logInfo($"Resolved lexicon: {nsid}");
                }
                catch (Exception ex)
                {
                    throw new LexiconResolutionException(
                        $"Failed to fetch lexicon record for '{nsid}' from {pdsUrl}. {ex.Message}", ex);
                }
            }
        }

        return results;
    }

    private async Task<string?> ResolveDnsToDidAsync(string dnsName, CancellationToken cancellationToken)
    {
        var records = await _dnsResolver.GetTxtRecordsAsync(dnsName, cancellationToken).ConfigureAwait(false);

        foreach (var record in records)
        {
            var trimmed = record.Trim();
            if (trimmed.StartsWith("did=", StringComparison.OrdinalIgnoreCase))
            {
                var did = trimmed.Substring(4);
                if (did.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
                    return did;
            }
        }

        return null;
    }

    private async Task<string?> ResolveDidToPdsAsync(string did, CancellationToken cancellationToken)
    {
        string didDocJson;

        if (did.StartsWith("did:plc:", StringComparison.OrdinalIgnoreCase))
        {
            var url = $"{_plcDirectoryUrl}/{did}";
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            didDocJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (did.StartsWith("did:web:", StringComparison.OrdinalIgnoreCase))
        {
            var domain = Uri.UnescapeDataString(did.Substring(8));
            var url = $"https://{domain}/.well-known/did.json";
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            didDocJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new LexiconResolutionException($"Unsupported DID method: {did}");
        }

        // Parse the DID document to find the PDS service endpoint
        return ExtractPdsEndpoint(didDocJson);
    }

    private static string? ExtractPdsEndpoint(string didDocJson)
    {
        using var doc = JsonDocument.Parse(didDocJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("service", out var services) || services.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var svc in services.EnumerateArray())
        {
            var type = svc.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
            var id = svc.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;

            if (type == "AtprotoPersonalDataServer" ||
                (id != null && id.EndsWith("#atproto_pds", StringComparison.Ordinal)))
            {
                if (svc.TryGetProperty("serviceEndpoint", out var endpoint))
                    return endpoint.GetString()?.TrimEnd('/');
            }
        }

        return null;
    }

    private async Task<string> FetchLexiconRecordAsync(
        string pdsUrl,
        string did,
        string nsid,
        CancellationToken cancellationToken)
    {
        var url = $"{pdsUrl}/xrpc/com.atproto.repo.getRecord" +
                  $"?repo={Uri.EscapeDataString(did)}" +
                  $"&collection=com.atproto.lexicon.schema" +
                  $"&rkey={Uri.EscapeDataString(nsid)}";

        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // The lexicon JSON is inside the "value" field of the getRecord response
        using var doc = JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("value", out var value))
            throw new LexiconResolutionException($"Response for '{nsid}' missing 'value' field");

        return value.GetRawText();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

/// <summary>
/// Exception thrown when lexicon resolution fails.
/// </summary>
internal sealed class LexiconResolutionException : Exception
{
    public LexiconResolutionException(string message) : base(message) { }
    public LexiconResolutionException(string message, Exception innerException) : base(message, innerException) { }
}
