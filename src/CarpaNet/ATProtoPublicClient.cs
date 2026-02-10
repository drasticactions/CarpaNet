using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Cbor;
using CarpaNet.EventStream;
using CarpaNet.Http;
using CarpaNet.Identity;

namespace CarpaNet;

/// <summary>
/// A public (unauthenticated) ATProtocol client for read-only operations.
/// Uses the public Bluesky AppView by default.
/// </summary>
/// <remarks>
/// <para>
/// This client is suitable for:
/// </para>
/// <list type="bullet">
/// <item><description>Fetching public profiles and posts</description></item>
/// <item><description>Reading public feeds</description></item>
/// <item><description>Resolving handles and DIDs</description></item>
/// <item><description>Any read-only public data access</description></item>
/// </list>
/// <para>
/// For authenticated operations (posting, following, etc.), use <see cref="ATProtoSessionClient"/>
/// or <see cref="ATProtoClient"/>.
/// </para>
/// </remarks>
public sealed class ATProtoPublicClient : IATProtoClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CborSerializerContext _cborContext;
    private bool _disposed;

    /// <inheritdoc/>
    public Uri BaseUrl { get; }

    /// <inheritdoc/>
    public bool IsAuthenticated => false;

    /// <inheritdoc/>
    public string? AuthenticatedDid => null;

    /// <inheritdoc/>
    public IdentityResolver? IdentityResolver { get; }

    /// <summary>
    /// Gets the optional list of labeler DIDs to accept labels from.
    /// These are included in the atproto-accept-labelers header.
    /// </summary>
    public IReadOnlyList<string>? LabelerDids { get; }

    /// <summary>
    /// Creates a new public ATProtocol client with a custom HttpClient.
    /// </summary>
    /// <param name="httpClient">The HttpClient to use for requests.</param>
    /// <param name="baseUrl">The base URL (defaults to public AppView).</param>
    /// <param name="identityResolver">Optional identity resolver for handle/DID resolution.</param>
    /// <param name="jsonOptions">The JSON serializer options (must include a source-generated IJsonTypeInfoResolver).</param>
    /// <param name="cborContext">The CBOR serializer context (must be a source-generated context).</param>
    /// <param name="labelerDids">Optional list of labeler DIDs to accept labels from.</param>
    public ATProtoPublicClient(
        HttpClient httpClient,
        JsonSerializerOptions jsonOptions,
        CborSerializerContext cborContext,
        Uri? baseUrl = null,
        IdentityResolver? identityResolver = null,
        IReadOnlyList<string>? labelerDids = null)
        : this(httpClient, ownsHttpClient: false, baseUrl ?? new Uri(BlueskyServices.PublicAppView), identityResolver, jsonOptions, cborContext, labelerDids)
    {
    }

    private ATProtoPublicClient(
        HttpClient httpClient,
        bool ownsHttpClient,
        Uri baseUrl,
        IdentityResolver? identityResolver,
        JsonSerializerOptions jsonOptions,
        CborSerializerContext cborContext,
        IReadOnlyList<string>? labelerDids)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
        BaseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        IdentityResolver = identityResolver;
        _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        _cborContext = cborContext ?? throw new ArgumentNullException(nameof(cborContext));
        LabelerDids = labelerDids;
    }

    /// <inheritdoc/>
    public async Task<TOutput> GetAsync<TOutput>(
        string nsid,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var url = XrpcHttpHandler.BuildUrl(BaseUrl, nsid, parameters);
        using var request = XrpcHttpHandler.CreateGetRequest(url, proxyServiceDid: null, LabelerDids);

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TOutput> GetAsync<TOutput>(
        string nsid,
        string proxyServiceDid,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // For public client, proxy header is typically not needed (public AppView handles routing)
        // but we support it for completeness
        var url = XrpcHttpHandler.BuildUrl(BaseUrl, nsid, parameters);
        using var request = XrpcHttpHandler.CreateGetRequest(url, proxyServiceDid, LabelerDids);

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Always thrown. Public client does not support write operations.
    /// </exception>
    public Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        TInput? input,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        throw new InvalidOperationException(
            "Public client does not support write operations. " +
            "Use ATProtoSessionClient or ATProtoClient for authenticated requests.");
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Always thrown. Public client does not support write operations.
    /// </exception>
    public Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        string proxyServiceDid,
        TInput? input,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        throw new InvalidOperationException(
            "Public client does not support write operations. " +
            "Use ATProtoSessionClient or ATProtoClient for authenticated requests.");
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TMessage> SubscribeAsync<TMessage>(
        string nsid,
        IReadOnlyDictionary<string, string>? parameters = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // For public subscriptions, typically use the Relay service
        // The caller can provide a different base URL if needed
        var eventStreamClient = new EventStreamClient(BaseUrl, _cborContext);
        try
        {
            var paramList = parameters != null
                ? new List<KeyValuePair<string, string?>>(
                    ((IEnumerable<KeyValuePair<string, string>>)parameters)
                        .Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value)))
                : null;

            await foreach (var message in eventStreamClient.SubscribeAsync<TMessage>(nsid, paramList, cancellationToken).ConfigureAwait(false))
            {
                yield return message;
            }
        }
        finally
        {
            eventStreamClient.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
#endif
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        // Dispose identity resolver if we own the HttpClient (created together)
        if (_ownsHttpClient && IdentityResolver != null)
        {
            IdentityResolver.Dispose();
        }
    }
}
