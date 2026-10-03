using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarpaNet.Identity;

/// <summary>
/// Resolves ATProtocol identities (handles and DIDs) to DID documents.
/// Supports did:plc and did:web methods, and handle resolution via DNS TXT, HTTPS well-known,
/// and (optionally) the <c>com.atproto.identity.resolveHandle</c> XRPC method.
/// </summary>
/// <remarks>
/// Handle resolution methods are tried in the order set by
/// <see cref="IdentityResolverOptions.HandleResolutionOrder"/> (default: DNS, well-known, XRPC).
/// When no DNS resolver is given, <see cref="DnsResolverDefaults.CreateDefault"/> selects one for
/// the platform (DNS-over-HTTPS in browsers, UDP elsewhere).
/// </remarks>
public sealed class IdentityResolver : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _plcDirectoryUrl;
    private readonly IDnsResolver _dnsResolver;
    private readonly IIdentityCache? _cache;
    private readonly HandleResolutionMethod[] _handleResolutionOrder;
    private readonly XrpcHandleResolver? _xrpcHandleResolver;
    private readonly ILogger<IdentityResolver> _logger;

    /// <summary>
    /// Default PLC directory URL.
    /// </summary>
    public const string DefaultPlcDirectory = "https://plc.directory";

    /// <summary>
    /// Creates a new IdentityResolver with default settings.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory for diagnostic logging.</param>
    public IdentityResolver(ILoggerFactory? loggerFactory = null)
        : this(new HttpClient(), ownsHttpClient: true, new IdentityResolverOptions(), loggerFactory)
    {
    }

    /// <summary>
    /// Creates a new IdentityResolver with a custom HttpClient.
    /// </summary>
    /// <param name="httpClient">The HttpClient to use for requests.</param>
    /// <param name="plcDirectoryUrl">The PLC directory URL (default: https://plc.directory).</param>
    /// <param name="dnsResolver">Optional custom DNS resolver for handle resolution. If null, <see cref="DnsResolverDefaults.CreateDefault"/> is used.</param>
    /// <param name="cache">Optional identity cache for caching resolved identities.</param>
    /// <param name="loggerFactory">Optional logger factory for diagnostic logging.</param>
    public IdentityResolver(
        HttpClient httpClient,
        string? plcDirectoryUrl = null,
        IDnsResolver? dnsResolver = null,
        IIdentityCache? cache = null,
        ILoggerFactory? loggerFactory = null)
        : this(
            httpClient,
            ownsHttpClient: false,
            new IdentityResolverOptions { PlcDirectoryUrl = plcDirectoryUrl, DnsResolver = dnsResolver, Cache = cache },
            loggerFactory)
    {
    }

    /// <summary>
    /// Creates a new IdentityResolver with the specified options.
    /// </summary>
    /// <param name="httpClient">The HttpClient to use for requests. It is not disposed by the resolver.</param>
    /// <param name="options">The resolver options.</param>
    /// <param name="loggerFactory">Optional logger factory for diagnostic logging.</param>
    public IdentityResolver(HttpClient httpClient, IdentityResolverOptions options, ILoggerFactory? loggerFactory = null)
        : this(httpClient, ownsHttpClient: false, options, loggerFactory)
    {
    }

    private IdentityResolver(
        HttpClient httpClient,
        bool ownsHttpClient,
        IdentityResolverOptions options,
        ILoggerFactory? loggerFactory)
    {
        if (httpClient == null)
            throw new ArgumentNullException(nameof(httpClient));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _plcDirectoryUrl = (options.PlcDirectoryUrl ?? DefaultPlcDirectory).TrimEnd('/');
        _dnsResolver = options.DnsResolver ?? DnsResolverDefaults.CreateDefault(httpClient);
        _cache = options.Cache;

        var order = options.HandleResolutionOrder;
        if (order == null || order.Count == 0)
            order = IdentityResolverOptions.DefaultHandleResolutionOrder;
        var methods = new List<HandleResolutionMethod>();
        foreach (var method in order)
        {
            if (!methods.Contains(method))
                methods.Add(method);
        }
        _handleResolutionOrder = methods.ToArray();

        if (!string.IsNullOrWhiteSpace(options.HandleResolutionServiceUrl))
            _xrpcHandleResolver = new XrpcHandleResolver(httpClient, options.HandleResolutionServiceUrl!);

        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<IdentityResolver>();
    }

    /// <summary>
    /// Gets the DNS resolver used for <see cref="HandleResolutionMethod.Dns"/>.
    /// </summary>
    public IDnsResolver DnsResolver => _dnsResolver;

    /// <summary>
    /// Gets the handle resolution methods, in the order they are tried.
    /// </summary>
    public IReadOnlyList<HandleResolutionMethod> HandleResolutionOrder => _handleResolutionOrder;

    /// <summary>
    /// Gets the identity cache, if configured.
    /// </summary>
    public IIdentityCache? Cache => _cache;

    /// <summary>
    /// Creates a new IdentityResolver with in-memory caching enabled.
    /// </summary>
    /// <param name="httpClient">Optional HttpClient to use for requests. If null, a new one will be created.</param>
    /// <param name="plcDirectoryUrl">The PLC directory URL (default: https://plc.directory).</param>
    /// <param name="dnsResolver">Optional custom DNS resolver for handle resolution. If null, <see cref="DnsResolverDefaults.CreateDefault"/> is used.</param>
    /// <param name="loggerFactory">Optional logger factory for diagnostic logging.</param>
    /// <returns>An IdentityResolver with caching enabled.</returns>
    public static IdentityResolver CreateWithCache(
        HttpClient? httpClient = null,
        string? plcDirectoryUrl = null,
        IDnsResolver? dnsResolver = null,
        ILoggerFactory? loggerFactory = null)
    {
        return CreateWithCache(new MemoryIdentityCache(), httpClient, plcDirectoryUrl, dnsResolver, loggerFactory);
    }

    /// <summary>
    /// Creates a new IdentityResolver with the specified cache.
    /// </summary>
    /// <param name="cache">The identity cache to use.</param>
    /// <param name="httpClient">Optional HttpClient to use for requests. If null, a new one will be created.</param>
    /// <param name="plcDirectoryUrl">The PLC directory URL (default: https://plc.directory).</param>
    /// <param name="dnsResolver">Optional custom DNS resolver for handle resolution. If null, <see cref="DnsResolverDefaults.CreateDefault"/> is used.</param>
    /// <param name="loggerFactory">Optional logger factory for diagnostic logging.</param>
    /// <returns>An IdentityResolver with the specified cache.</returns>
    public static IdentityResolver CreateWithCache(
        IIdentityCache cache,
        HttpClient? httpClient = null,
        string? plcDirectoryUrl = null,
        IDnsResolver? dnsResolver = null,
        ILoggerFactory? loggerFactory = null)
    {
        if (cache == null)
            throw new ArgumentNullException(nameof(cache));

        var ownsHttpClient = httpClient == null;
        httpClient ??= new HttpClient();

        return new IdentityResolver(
            httpClient,
            ownsHttpClient,
            new IdentityResolverOptions { PlcDirectoryUrl = plcDirectoryUrl, DnsResolver = dnsResolver, Cache = cache },
            loggerFactory);
    }

    /// <summary>
    /// Resolves an identifier (handle or DID) to a DID document.
    /// </summary>
    /// <remarks>
    /// For a handle, the DID document's handle claim (<c>alsoKnownAs</c>) must match the handle,
    /// or an <see cref="IdentityResolutionException"/> is thrown. This check runs for every
    /// handle resolution method. When the DID came from <see cref="HandleResolutionMethod.Xrpc"/>,
    /// the handle-to-DID direction is trusted from the service, not verified by DNS or well-known.
    /// </remarks>
    /// <param name="identifier">A handle (e.g., "alice.bsky.social") or DID (e.g., "did:plc:...").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved DID document.</returns>
    public Task<DidDocument> ResolveAsync(string identifier, CancellationToken cancellationToken = default)
    {
        return ResolveAsync(identifier, skipCache: false, cancellationToken);
    }

    /// <summary>
    /// Resolves an identifier (handle or DID) to a DID document.
    /// </summary>
    /// <param name="identifier">A handle (e.g., "alice.bsky.social") or DID (e.g., "did:plc:...").</param>
    /// <param name="skipCache">If true, bypasses the cache and forces a fresh resolution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved DID document.</returns>
    public async Task<DidDocument> ResolveAsync(string identifier, bool skipCache, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier cannot be empty", nameof(identifier));

        identifier = identifier.Trim();

        // Remove @ prefix if present (common in UIs)
        if (identifier.StartsWith("@"))
            identifier = identifier.Substring(1);

        // Check if it's a DID or handle
        if (identifier.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveDidAsync(identifier, skipCache, cancellationToken).ConfigureAwait(false);
        }

        // It's a handle - resolve to DID first
        _logger.LogDebug("Resolving handle {Handle}", identifier);
        var did = await ResolveHandleAsync(identifier, skipCache, cancellationToken).ConfigureAwait(false);
        var doc = await ResolveDidAsync(did, skipCache, cancellationToken).ConfigureAwait(false);

        // Verify bidirectional link
        if (doc.Handle != null && !doc.Handle.Equals(identifier, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Bidirectional handle mismatch for {Handle}", identifier);
            throw new IdentityResolutionException(
                $"Handle mismatch: resolved handle '{doc.Handle}' does not match '{identifier}'");
        }

        return doc;
    }

    /// <summary>
    /// Resolves a DID to its DID document.
    /// </summary>
    /// <param name="did">The DID to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The DID document.</returns>
    public Task<DidDocument> ResolveDidAsync(string did, CancellationToken cancellationToken = default)
    {
        return ResolveDidAsync(did, skipCache: false, cancellationToken);
    }

    /// <summary>
    /// Resolves a DID to its DID document.
    /// </summary>
    /// <param name="did">The DID to resolve.</param>
    /// <param name="skipCache">If true, bypasses the cache and forces a fresh resolution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The DID document.</returns>
    public async Task<DidDocument> ResolveDidAsync(string did, bool skipCache, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(did))
            throw new ArgumentException("DID cannot be empty", nameof(did));

        if (!did.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid DID format", nameof(did));

        // Check cache first (unless skipping)
        if (!skipCache && _cache != null)
        {
            var cached = await _cache.GetDidDocumentAsync(did, cancellationToken).ConfigureAwait(false);
            if (cached != null)
            {
                _logger.LogTrace("Cache hit for {Key}", did);
                return cached;
            }

            _logger.LogTrace("Cache miss for {Key}", did);
        }

        // Parse DID method
        var colonIndex = did.IndexOf(':', 4);
        if (colonIndex < 0)
            throw new ArgumentException("Invalid DID format: missing method", nameof(did));

        var method = did.Substring(4, colonIndex - 4).ToLowerInvariant();
        _logger.LogDebug("Resolving DID {Did} via {Method}", did, method);

        var document = method switch
        {
            "plc" => await ResolvePlcDidAsync(did, cancellationToken).ConfigureAwait(false),
            "web" => await ResolveWebDidAsync(did, cancellationToken).ConfigureAwait(false),
            _ => throw new IdentityResolutionException($"Unsupported DID method: {method}")
        };

        // Cache the result
        if (_cache != null)
        {
            await _cache.SetDidDocumentAsync(did, document, cancellationToken).ConfigureAwait(false);
        }

        return document;
    }

    /// <summary>
    /// Resolves a did:plc to its DID document.
    /// </summary>
    public async Task<DidDocument> ResolvePlcDidAsync(string did, CancellationToken cancellationToken = default)
    {
        if (!did.StartsWith("did:plc:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Not a did:plc", nameof(did));

        var url = $"{_plcDirectoryUrl}/{did}";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return DidDocument.FromJson(json);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Failed to resolve did:plc {Did}", did);
            throw new IdentityResolutionException($"Failed to resolve did:plc '{did}'", ex);
        }
    }

    /// <summary>
    /// Resolves a did:web to its DID document.
    /// </summary>
    public async Task<DidDocument> ResolveWebDidAsync(string did, CancellationToken cancellationToken = default)
    {
        if (!did.StartsWith("did:web:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Not a did:web", nameof(did));

        // did:web:example.com -> https://example.com/.well-known/did.json
        // did:web:example.com%3A8080 -> https://example.com:8080/.well-known/did.json (port encoded)
        var domain = did.Substring(8);
        domain = Uri.UnescapeDataString(domain); // Handle percent-encoded characters like %3A for ':'

        var url = $"https://{domain}/.well-known/did.json";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return DidDocument.FromJson(json);
        }
        catch (HttpRequestException ex)
        {
            throw new IdentityResolutionException($"Failed to resolve did:web '{did}'", ex);
        }
    }

    /// <summary>
    /// Resolves a handle to a DID using the configured handle resolution methods
    /// (by default DNS TXT, then HTTPS well-known, then XRPC if a service is configured).
    /// </summary>
    /// <param name="handle">The handle to resolve (e.g., "alice.bsky.social").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The DID.</returns>
    public Task<string> ResolveHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        return ResolveHandleAsync(handle, skipCache: false, cancellationToken);
    }

    /// <summary>
    /// Resolves a handle to a DID using the configured handle resolution methods
    /// (by default DNS TXT, then HTTPS well-known, then XRPC if a service is configured).
    /// </summary>
    /// <param name="handle">The handle to resolve (e.g., "alice.bsky.social").</param>
    /// <param name="skipCache">If true, bypasses the cache and forces a fresh resolution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The DID.</returns>
    public async Task<string> ResolveHandleAsync(string handle, bool skipCache, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw new ArgumentException("Handle cannot be empty", nameof(handle));

        handle = handle.ToLowerInvariant().Trim();

        // Remove @ prefix if present
        if (handle.StartsWith("@"))
            handle = handle.Substring(1);

        // Validate handle format
        if (!IsValidHandle(handle))
            throw new ArgumentException($"Invalid handle format: {handle}", nameof(handle));

        // Check cache first (unless skipping)
        if (!skipCache && _cache != null)
        {
            var cachedDid = await _cache.GetHandleDidAsync(handle, cancellationToken).ConfigureAwait(false);
            if (cachedDid != null)
            {
                _logger.LogTrace("Cache hit for {Key}", handle);
                return cachedDid;
            }

            _logger.LogTrace("Cache miss for {Key}", handle);
        }

        foreach (var method in _handleResolutionOrder)
        {
            var did = method switch
            {
                HandleResolutionMethod.Dns => await TryResolveHandleDnsAsync(handle, cancellationToken).ConfigureAwait(false),
                HandleResolutionMethod.WellKnown => await TryResolveHandleHttpsAsync(handle, cancellationToken).ConfigureAwait(false),
                HandleResolutionMethod.Xrpc => await TryResolveHandleXrpcAsync(handle, cancellationToken).ConfigureAwait(false),
                _ => null
            };

            if (string.IsNullOrEmpty(did))
                continue;

            _logger.LogDebug("{Method} resolution found {Did} for {Handle}", method, did, handle);

            // Cache the result
            if (_cache != null)
            {
                await _cache.SetHandleDidAsync(handle, did!, cancellationToken).ConfigureAwait(false);
            }

            return did!;
        }

        _logger.LogError("Failed to resolve handle {Handle}", handle);
        throw new IdentityResolutionException($"Failed to resolve handle '{handle}': no valid DID found");
    }

    /// <summary>
    /// Tries to resolve a handle via DNS TXT record.
    /// </summary>
    private async Task<string?> TryResolveHandleDnsAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            var txtRecordName = $"_atproto.{handle}";
            var records = await _dnsResolver.GetTxtRecordsAsync(txtRecordName, cancellationToken).ConfigureAwait(false);

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
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // DNS resolution failed, will try the next method
            _logger.LogDebug(ex, "DNS resolution failed for {Handle}", handle);
        }

        return null;
    }

    /// <summary>
    /// Tries to resolve a handle via HTTPS well-known endpoint.
    /// </summary>
    private async Task<string?> TryResolveHandleHttpsAsync(string handle, CancellationToken cancellationToken)
    {
        var url = $"https://{handle}/.well-known/atproto-did";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var did = content.Trim();

            if (did.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
                return did;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HTTPS resolution failed, will try the next method
            _logger.LogDebug(ex, "Well-known resolution failed for {Handle}", handle);
        }

        return null;
    }

    /// <summary>
    /// Tries to resolve a handle via the configured XRPC service.
    /// The result is trusted from the service, not verified against DNS or well-known.
    /// </summary>
    private async Task<string?> TryResolveHandleXrpcAsync(string handle, CancellationToken cancellationToken)
    {
        if (_xrpcHandleResolver == null)
            return null; // No service configured

        try
        {
            return await _xrpcHandleResolver.ResolveHandleAsync(handle, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "XRPC resolution via {Service} failed for {Handle}", _xrpcHandleResolver.ServiceUrl, handle);
        }

        return null;
    }

    /// <summary>
    /// Validates handle syntax according to ATProtocol spec.
    /// </summary>
    public static bool IsValidHandle(string handle)
    {
        if (string.IsNullOrEmpty(handle) || handle.Length > 253)
            return false;

        // Must have at least two segments
        var segments = handle.Split('.');
        if (segments.Length < 2)
            return false;

        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment) || segment.Length > 63)
                return false;

            // Must start and end with alphanumeric
            if (!char.IsLetterOrDigit(segment[0]) || !char.IsLetterOrDigit(segment[segment.Length - 1]))
                return false;

            // Only alphanumeric and hyphens allowed
            foreach (var c in segment)
            {
                if (!char.IsLetterOrDigit(c) && c != '-')
                    return false;
            }
        }

        // Last segment (TLD) must not start with a digit
        var lastSegment = segments[segments.Length - 1];
        if (char.IsDigit(lastSegment[0]))
            return false;

        return true;
    }

    /// <summary>
    /// Validates DID syntax according to ATProtocol spec.
    /// </summary>
    public static bool IsValidDid(string did)
    {
        if (string.IsNullOrEmpty(did) || did.Length > 2048)
            return false;

        // Basic regex check
        return Regex.IsMatch(did, @"^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._-]$");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

/// <summary>
/// Interface for DNS resolution (allows for testing and custom implementations).
/// </summary>
public interface IDnsResolver
{
    /// <summary>
    /// Gets TXT records for a domain name.
    /// </summary>
    Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Exception thrown when identity resolution fails.
/// </summary>
public class IdentityResolutionException : Exception
{
    /// <summary>
    /// Creates a new IdentityResolutionException.
    /// </summary>
    public IdentityResolutionException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new IdentityResolutionException with an inner exception.
    /// </summary>
    public IdentityResolutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
