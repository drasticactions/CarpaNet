using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Auth;
using CarpaNet.Cbor;
using CarpaNet.EventStream;
using CarpaNet.Http;
using CarpaNet.Identity;

namespace CarpaNet;

/// <summary>
/// A unified ATProtocol client supporting multiple authentication modes.
/// </summary>
/// <remarks>
/// <para>
/// This is the main client for interacting with ATProtocol services. It supports:
/// </para>
/// <list type="bullet">
/// <item><description>Public (unauthenticated) access - via <see cref="CreatePublic"/></description></item>
/// <item><description>Session-based authentication (App Passwords) - via <see cref="CreateWithSessionAsync"/></description></item>
/// <item><description>Custom token providers - via constructor with <see cref="ITokenProvider"/></description></item>
/// </list>
/// </remarks>
public sealed class ATProtoClient : IATProtoClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ITokenProvider? _tokenProvider;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CborSerializerContext _cborContext;
    private readonly bool _autoRetryOnAuthFailure;
    private bool _disposed;

    /// <inheritdoc/>
    public Uri BaseUrl { get; }

    /// <inheritdoc/>
    public bool IsAuthenticated => _tokenProvider?.HasValidToken ?? false;

    /// <inheritdoc/>
    public string? AuthenticatedDid => _tokenProvider?.CurrentDid;

    /// <inheritdoc/>
    public IdentityResolver? IdentityResolver { get; }

    /// <summary>
    /// Gets the optional list of labeler DIDs to accept labels from.
    /// </summary>
    public IReadOnlyList<string>? LabelerDids { get; }

    /// <summary>
    /// Gets the token provider, if any.
    /// </summary>
    public ITokenProvider? TokenProvider => _tokenProvider;

    #region Factory Methods

    /// <summary>
    /// Creates a public (unauthenticated) client using the Bluesky public AppView.
    /// </summary>
    /// <param name="options">Optional configuration options.</param>
    /// <returns>A public ATProtoClient.</returns>
    public static ATProtoClient CreatePublic(ATProtoClientOptions? options = null)
    {
        options ??= new ATProtoClientOptions();
        options.BaseUrl ??= new Uri(BlueskyServices.PublicAppView);
        options.TokenProvider = null; // Ensure no auth
        return new ATProtoClient(options);
    }

    /// <summary>
    /// Creates an authenticated client using session-based authentication (App Passwords).
    /// </summary>
    /// <param name="identifier">The user identifier (handle, email, or DID).</param>
    /// <param name="password">The password or App Password.</param>
    /// <param name="serviceUrl">The service URL (default: Bluesky Entryway).</param>
    /// <param name="options">Optional configuration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An authenticated ATProtoClient.</returns>
    public static async Task<ATProtoClient> CreateWithSessionAsync(
        string identifier,
        string password,
        Uri? serviceUrl = null,
        ATProtoClientOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = options?.Clone() ?? new ATProtoClientOptions();

        // Create HttpClient if not provided
        var httpClient = options.HttpClient ?? new HttpClient();
        var ownsHttpClient = options.HttpClient == null;

        if (options.Timeout.HasValue)
        {
            httpClient.Timeout = options.Timeout.Value;
        }

        // Create session token provider and login
        var tokenProvider = new SessionTokenProvider(httpClient);
        try
        {
            await tokenProvider.LoginAsync(identifier, password, serviceUrl, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            tokenProvider.Dispose();
            if (ownsHttpClient)
            {
                httpClient.Dispose();
            }
            throw;
        }

        // Use PDS URL from session as base URL
        options.BaseUrl = tokenProvider.PdsUrl;
        options.TokenProvider = tokenProvider;
        options.HttpClient = httpClient;

        var client = new ATProtoClient(options, ownsHttpClient, ownsTokenProvider: true);
        return client;
    }

    /// <summary>
    /// Creates a client with a restored session.
    /// </summary>
    /// <param name="accessJwt">The stored access JWT.</param>
    /// <param name="refreshJwt">The stored refresh JWT.</param>
    /// <param name="did">The user's DID.</param>
    /// <param name="handle">The user's handle.</param>
    /// <param name="pdsUrl">The PDS URL.</param>
    /// <param name="options">Optional configuration options.</param>
    /// <returns>An authenticated ATProtoClient with restored session.</returns>
    public static ATProtoClient CreateWithRestoredSession(
        string accessJwt,
        string refreshJwt,
        string did,
        string? handle,
        Uri pdsUrl,
        ATProtoClientOptions? options = null)
    {
        options = options?.Clone() ?? new ATProtoClientOptions();

        var httpClient = options.HttpClient ?? new HttpClient();
        var ownsHttpClient = options.HttpClient == null;

        if (options.Timeout.HasValue)
        {
            httpClient.Timeout = options.Timeout.Value;
        }

        var tokenProvider = new SessionTokenProvider(httpClient);
        tokenProvider.RestoreSession(accessJwt, refreshJwt, did, handle, pdsUrl);

        options.BaseUrl = pdsUrl;
        options.TokenProvider = tokenProvider;
        options.HttpClient = httpClient;

        return new ATProtoClient(options, ownsHttpClient, ownsTokenProvider: true);
    }

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new ATProtoClient with the specified options.
    /// </summary>
    /// <param name="options">The configuration options.</param>
    public ATProtoClient(ATProtoClientOptions options)
        : this(options, ownsHttpClient: options.HttpClient == null, ownsTokenProvider: false)
    {
    }

    private ATProtoClient(ATProtoClientOptions options, bool ownsHttpClient, bool ownsTokenProvider)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        _httpClient = options.HttpClient ?? new HttpClient();
        _ownsHttpClient = ownsHttpClient;

        if (options.Timeout.HasValue && ownsHttpClient)
        {
            _httpClient.Timeout = options.Timeout.Value;
        }

        _tokenProvider = options.TokenProvider;
        _jsonOptions = options.JsonOptions ?? throw new ArgumentException("JsonOptions must be provided.", nameof(options));
        _cborContext = options.CborContext ?? throw new ArgumentException("CborContext must be provided.", nameof(options));
        _autoRetryOnAuthFailure = options.AutoRetryOnAuthFailure;
        LabelerDids = options.LabelerDids;

        // Determine base URL
        BaseUrl = options.BaseUrl
            ?? _tokenProvider?.PdsUrl
            ?? new Uri(BlueskyServices.PublicAppView);

        // Create or use identity resolver
        if (options.IdentityResolver != null)
        {
            IdentityResolver = options.IdentityResolver;
        }
        else if (options.CreateIdentityResolver)
        {
            IdentityResolver = new IdentityResolver(_httpClient);
        }

        // If we own the token provider, subscribe to refresh events
        if (ownsTokenProvider && _tokenProvider is SessionTokenProvider sessionProvider)
        {
            // Store for cleanup
            _ownsTokenProvider = true;
        }
    }

    private readonly bool _ownsTokenProvider;

    #endregion

    #region IATProtoClient Implementation

    /// <inheritdoc/>
    public async Task<TOutput> GetAsync<TOutput>(
        string nsid,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var url = XrpcHttpHandler.BuildUrl(BaseUrl, nsid, parameters);
        using var request = XrpcHttpHandler.CreateGetRequest(url, proxyServiceDid: null, LabelerDids);
        await AddAuthHeaderAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
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

        var url = XrpcHttpHandler.BuildUrl(BaseUrl, nsid, parameters);
        using var request = XrpcHttpHandler.CreateGetRequest(url, proxyServiceDid, LabelerDids);
        await AddAuthHeaderAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        return await XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        TInput? input,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_tokenProvider == null)
        {
            throw new InvalidOperationException(
                "Cannot make POST requests without authentication. " +
                "Use CreateWithSessionAsync or provide a TokenProvider.");
        }

        var url = XrpcHttpHandler.BuildUrl(BaseUrl, nsid);
        using var request = XrpcHttpHandler.CreatePostRequest(url, input, _jsonOptions, proxyServiceDid: null, LabelerDids);
        await AddAuthHeaderAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        return await XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        string proxyServiceDid,
        TInput? input,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_tokenProvider == null)
        {
            throw new InvalidOperationException(
                "Cannot make POST requests without authentication. " +
                "Use CreateWithSessionAsync or provide a TokenProvider.");
        }

        var url = XrpcHttpHandler.BuildUrl(BaseUrl, nsid);
        using var request = XrpcHttpHandler.CreatePostRequest(url, input, _jsonOptions, proxyServiceDid, LabelerDids);
        await AddAuthHeaderAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        return await XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TMessage> SubscribeAsync<TMessage>(
        string nsid,
        IReadOnlyDictionary<string, string>? parameters = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

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

    #endregion

    #region Private Methods

    private async Task AddAuthHeaderAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_tokenProvider == null)
            return;

        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Retry on 401 if auto-retry is enabled and we have a token provider
        if (_autoRetryOnAuthFailure &&
            response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            _tokenProvider != null)
        {
            try
            {
                await _tokenProvider.RefreshAsync(cancellationToken).ConfigureAwait(false);

                // Create a new request and retry
                using var retryRequest = CloneRequest(request);
                await AddAuthHeaderAsync(retryRequest, cancellationToken).ConfigureAwait(false);

                response.Dispose();
                return await _httpClient.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (AuthenticationException)
            {
                // Refresh failed, return original 401 response
            }
            catch (InvalidOperationException)
            {
                // No refresh token available
            }
        }

        return response;
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        foreach (var header in original.Headers)
        {
            if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (original.Content != null)
        {
            var contentBytes = original.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            clone.Content = new ByteArrayContent(contentBytes);

            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
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

    #endregion

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_ownsTokenProvider && _tokenProvider is IDisposable disposableProvider)
        {
            disposableProvider.Dispose();
        }

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();

            // Also dispose identity resolver if we created it
            IdentityResolver?.Dispose();
        }
    }
}
