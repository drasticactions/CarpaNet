using System;
using System.Collections.Generic;
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
using CarpaNet.Storage;

namespace CarpaNet;

/// <summary>
/// An authenticated ATProtocol client that uses session tokens (App Passwords).
/// </summary>
/// <remarks>
/// <para>
/// This client is suitable for:
/// </para>
/// <list type="bullet">
/// <item><description>Personal tools and scripts</description></item>
/// <item><description>Bots with a single account</description></item>
/// <item><description>Applications using App Passwords</description></item>
/// </list>
/// <para>
/// For OAuth-based authentication in third-party apps, consider using the OAuth package.
/// </para>
/// </remarks>
public sealed class ATProtoSessionClient : IATProtoClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SessionTokenProvider _tokenProvider;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CborSerializerContext _cborContext;
    private bool _disposed;

    /// <inheritdoc/>
    public Uri BaseUrl => _tokenProvider.PdsUrl ?? new Uri(BlueskyServices.Entryway);

    /// <inheritdoc/>
    public bool IsAuthenticated => _tokenProvider.HasValidToken;

    /// <inheritdoc/>
    public string? AuthenticatedDid => _tokenProvider.CurrentDid;

    /// <inheritdoc/>
    public IdentityResolver? IdentityResolver { get; }

    /// <summary>
    /// Gets the handle of the authenticated user.
    /// </summary>
    public string? Handle => _tokenProvider.Handle;

    /// <summary>
    /// Gets the token provider for advanced token management.
    /// </summary>
    public SessionTokenProvider TokenProvider => _tokenProvider;

    /// <summary>
    /// Gets the optional list of labeler DIDs to accept labels from.
    /// </summary>
    public IReadOnlyList<string>? LabelerDids { get; }

    /// <summary>
    /// Creates a new session client with a custom HttpClient.
    /// The client is not yet authenticated - call LoginAsync first.
    /// </summary>
    /// <param name="httpClient">The HttpClient to use for requests.</param>
    /// <param name="jsonOptions">The JSON serializer options (must include a source-generated IJsonTypeInfoResolver).</param>
    /// <param name="cborContext">The CBOR serializer context (must be a source-generated context).</param>
    /// <param name="identityResolver">Optional identity resolver.</param>
    /// <param name="labelerDids">Optional list of labeler DIDs.</param>
    /// <param name="sessionStore">Optional session store for automatic persistence.</param>
    public ATProtoSessionClient(
        HttpClient httpClient,
        JsonSerializerOptions jsonOptions,
        CborSerializerContext cborContext,
        IdentityResolver? identityResolver = null,
        IReadOnlyList<string>? labelerDids = null,
        ISessionStore? sessionStore = null)
        : this(httpClient, ownsHttpClient: false, identityResolver, jsonOptions, cborContext, labelerDids, sessionStore)
    {
    }

    private ATProtoSessionClient(
        HttpClient httpClient,
        bool ownsHttpClient,
        IdentityResolver? identityResolver,
        JsonSerializerOptions jsonOptions,
        CborSerializerContext cborContext,
        IReadOnlyList<string>? labelerDids,
        ISessionStore? sessionStore)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
        _tokenProvider = new SessionTokenProvider(httpClient, sessionStore: sessionStore);
        IdentityResolver = identityResolver;
        _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        _cborContext = cborContext ?? throw new ArgumentNullException(nameof(cborContext));
        LabelerDids = labelerDids;
    }

    /// <summary>
    /// Authenticates with the ATProtocol service using credentials.
    /// </summary>
    /// <param name="identifier">The user identifier (handle, email, or DID).</param>
    /// <param name="password">The password or App Password.</param>
    /// <param name="serviceUrl">The service URL (default: Bluesky Entryway).</param>
    /// <param name="authFactorToken">Optional 2FA token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session response with user info.</returns>
    public Task<SessionResponse> LoginAsync(
        string identifier,
        string password,
        Uri? serviceUrl = null,
        string? authFactorToken = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _tokenProvider.LoginAsync(identifier, password, serviceUrl, authFactorToken, cancellationToken);
    }

    /// <summary>
    /// Restores a session from stored tokens.
    /// Call RefreshAsync to verify the tokens are still valid.
    /// </summary>
    /// <param name="accessJwt">The stored access JWT.</param>
    /// <param name="refreshJwt">The stored refresh JWT.</param>
    /// <param name="did">The user's DID.</param>
    /// <param name="handle">The user's handle.</param>
    /// <param name="pdsUrl">The PDS URL.</param>
    public void RestoreSession(string accessJwt, string refreshJwt, string did, string? handle, Uri pdsUrl)
    {
        ThrowIfDisposed();
        _tokenProvider.RestoreSession(accessJwt, refreshJwt, did, handle, pdsUrl);
    }

    /// <summary>
    /// Restores a session from the session store by DID.
    /// Requires a session store to be configured.
    /// </summary>
    /// <param name="sub">The user's DID (subject).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the session was restored, false if no stored session was found.</returns>
    public Task<bool> RestoreSessionAsync(string sub, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _tokenProvider.RestoreSessionAsync(sub, cancellationToken);
    }

    /// <summary>
    /// Logs out and clears the current session.
    /// Optionally deletes the session on the server.
    /// </summary>
    /// <param name="deleteOnServer">Whether to call deleteSession on the server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task LogoutAsync(bool deleteOnServer = true, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (deleteOnServer && _tokenProvider.HasValidToken && _tokenProvider.PdsUrl != null)
        {
            try
            {
                var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                {
                    var url = XrpcHttpHandler.BuildUrl(_tokenProvider.PdsUrl, "com.atproto.server.deleteSession");
                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    // Fire and forget - don't throw if delete fails
                    await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore errors when deleting session
            }
        }

        await _tokenProvider.ClearSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TOutput> GetAsync<TOutput>(
        string nsid,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureAuthenticated();

        var url = XrpcHttpHandler.BuildUrl(BaseUrl, nsid, parameters);
        using var request = XrpcHttpHandler.CreateGetRequest(url, proxyServiceDid: null, LabelerDids);
        await AddAuthHeaderAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendWithRetryOnAuthFailureAsync(request, cancellationToken).ConfigureAwait(false);
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
        EnsureAuthenticated();

        var url = XrpcHttpHandler.BuildUrl(BaseUrl, nsid, parameters);
        using var request = XrpcHttpHandler.CreateGetRequest(url, proxyServiceDid, LabelerDids);
        await AddAuthHeaderAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendWithRetryOnAuthFailureAsync(request, cancellationToken).ConfigureAwait(false);
        return await XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        TInput? input,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureAuthenticated();

        var url = XrpcHttpHandler.BuildUrl(BaseUrl, nsid);
        using var request = XrpcHttpHandler.CreatePostRequest(url, input, _jsonOptions, proxyServiceDid: null, LabelerDids);
        await AddAuthHeaderAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendWithRetryOnAuthFailureAsync(request, cancellationToken).ConfigureAwait(false);
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
        EnsureAuthenticated();

        var url = XrpcHttpHandler.BuildUrl(BaseUrl, nsid);
        using var request = XrpcHttpHandler.CreatePostRequest(url, input, _jsonOptions, proxyServiceDid, LabelerDids);
        await AddAuthHeaderAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendWithRetryOnAuthFailureAsync(request, cancellationToken).ConfigureAwait(false);
        return await XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TMessage> SubscribeAsync<TMessage>(
        string nsid,
        IReadOnlyDictionary<string, string>? parameters = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // For subscriptions, use the relay or the configured base URL
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

    private async Task AddAuthHeaderAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryOnAuthFailureAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Retry on 401 after refreshing token
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            try
            {
                await _tokenProvider.RefreshAsync(cancellationToken).ConfigureAwait(false);

                // Create a new request (can't reuse the old one after sending)
                using var retryRequest = CloneRequest(request);
                await AddAuthHeaderAsync(retryRequest, cancellationToken).ConfigureAwait(false);

                response.Dispose();
                return await _httpClient.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (AuthenticationException)
            {
                // Refresh failed, return original 401 response
            }
        }

        return response;
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        // Copy headers
        foreach (var header in original.Headers)
        {
            // Skip Authorization header - it will be re-added
            if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Copy content if present
        if (original.Content != null)
        {
            // For JSON content, we can read and re-create
            var contentBytes = original.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            clone.Content = new ByteArrayContent(contentBytes);

            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private void EnsureAuthenticated()
    {
        if (!IsAuthenticated)
        {
            throw new InvalidOperationException(
                "Client is not authenticated. Call LoginAsync or RestoreSession first.");
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
        _tokenProvider.Dispose();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        // Dispose identity resolver if we own it
        if (_ownsHttpClient && IdentityResolver != null)
        {
            IdentityResolver.Dispose();
        }
    }
}
