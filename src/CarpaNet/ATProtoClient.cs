using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Auth;
using CarpaNet.Http;
using CarpaNet.Identity;
using CarpaNet.Storage;

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
    private readonly ATProtoClientCore _core;
    private readonly bool _ownsHttpClient;
    private readonly ITokenProvider? _tokenProvider;
    private readonly bool _autoRetryOnAuthFailure;
    private readonly Uri? _configuredBaseUrl;
    private bool _disposed;

    /// <inheritdoc/>
    public Uri BaseUrl => _configuredBaseUrl ?? _tokenProvider?.PdsUrl ?? new Uri(BlueskyServices.PublicAppView);

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

    /// <summary>
    /// Gets the handle of the authenticated user, if using session-based authentication.
    /// </summary>
    public string? Handle => (_tokenProvider as SessionTokenProvider)?.Handle;

    #region Session Lifecycle

    /// <summary>
    /// Authenticates with the ATProtocol service using credentials.
    /// Only works when the client was created with a <see cref="SessionTokenProvider"/>.
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
        var sessionProvider = GetSessionTokenProvider();
        return sessionProvider.LoginAsync(identifier, password, serviceUrl, authFactorToken, cancellationToken);
    }

    /// <summary>
    /// Restores a session from stored tokens.
    /// Only works when the client was created with a <see cref="SessionTokenProvider"/>.
    /// </summary>
    /// <param name="accessJwt">The stored access JWT.</param>
    /// <param name="refreshJwt">The stored refresh JWT.</param>
    /// <param name="did">The user's DID.</param>
    /// <param name="handle">The user's handle.</param>
    /// <param name="pdsUrl">The PDS URL.</param>
    public void RestoreSession(string accessJwt, string refreshJwt, string did, string? handle, Uri pdsUrl)
    {
        ThrowIfDisposed();
        var sessionProvider = GetSessionTokenProvider();
        sessionProvider.RestoreSession(accessJwt, refreshJwt, did, handle, pdsUrl);
    }

    /// <summary>
    /// Restores a session from the session store by DID.
    /// Requires a session store to be configured on the <see cref="SessionTokenProvider"/>.
    /// </summary>
    /// <param name="sub">The user's DID (subject).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the session was restored, false if no stored session was found.</returns>
    public Task<bool> RestoreSessionAsync(string sub, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var sessionProvider = GetSessionTokenProvider();
        return sessionProvider.RestoreSessionAsync(sub, cancellationToken);
    }

    /// <summary>
    /// Logs out and clears the current session.
    /// Optionally deletes the session on the server.
    /// Only works when the client was created with a <see cref="SessionTokenProvider"/>.
    /// </summary>
    /// <param name="deleteOnServer">Whether to call deleteSession on the server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task LogoutAsync(bool deleteOnServer = true, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var sessionProvider = GetSessionTokenProvider();

        if (deleteOnServer && sessionProvider.HasValidToken && sessionProvider.PdsUrl != null)
        {
            try
            {
                var token = await sessionProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                {
                    var url = XrpcHttpHandler.BuildUrl(sessionProvider.PdsUrl, "com.atproto.server.deleteSession");
                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    // Fire and forget - don't throw if delete fails
                    await _core.HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore errors when deleting session
            }
        }

        await sessionProvider.ClearSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    private SessionTokenProvider GetSessionTokenProvider()
    {
        if (_tokenProvider is SessionTokenProvider sessionProvider)
        {
            return sessionProvider;
        }

        throw new InvalidOperationException(
            "Session lifecycle methods require a SessionTokenProvider. " +
            "Use CreateWithSessionAsync or provide a SessionTokenProvider in options.");
    }

    #endregion

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
        var tokenProvider = new SessionTokenProvider(httpClient, sessionStore: options.SessionStore);
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

        var tokenProvider = new SessionTokenProvider(httpClient, sessionStore: options.SessionStore);
        tokenProvider.RestoreSession(accessJwt, refreshJwt, did, handle, pdsUrl);

        options.BaseUrl = pdsUrl;
        options.TokenProvider = tokenProvider;
        options.HttpClient = httpClient;

        return new ATProtoClient(options, ownsHttpClient, ownsTokenProvider: true);
    }

    /// <summary>
    /// Creates an unauthenticated client configured with a session store for later login via <see cref="LoginAsync"/>.
    /// </summary>
    /// <param name="options">Configuration options. Must include <see cref="ATProtoClientOptions.SessionStore"/>.</param>
    /// <returns>An unauthenticated ATProtoClient ready for <see cref="LoginAsync"/>.</returns>
    public static ATProtoClient CreateWithSessionStore(ATProtoClientOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        options = options.Clone();

        var httpClient = options.HttpClient ?? new HttpClient();
        var ownsHttpClient = options.HttpClient == null;

        if (options.Timeout.HasValue)
        {
            httpClient.Timeout = options.Timeout.Value;
        }

        var tokenProvider = new SessionTokenProvider(httpClient, sessionStore: options.SessionStore);
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

        var httpClient = options.HttpClient ?? new HttpClient();
        _ownsHttpClient = ownsHttpClient;

        if (options.Timeout.HasValue && ownsHttpClient)
        {
            httpClient.Timeout = options.Timeout.Value;
        }

        var jsonOptions = options.JsonOptions ?? throw new ArgumentException("JsonOptions must be provided.", nameof(options));
        var cborContext = options.CborContext ?? throw new ArgumentException("CborContext must be provided.", nameof(options));

        _core = new ATProtoClientCore(httpClient, jsonOptions, cborContext);
        _tokenProvider = options.TokenProvider;
        _autoRetryOnAuthFailure = options.AutoRetryOnAuthFailure;
        LabelerDids = options.LabelerDids;

        // Store configured base URL (null means dynamic - will use token provider's PDS URL)
        _configuredBaseUrl = options.BaseUrl ?? _tokenProvider?.PdsUrl;

        // Create or use identity resolver
        if (options.IdentityResolver != null)
        {
            IdentityResolver = options.IdentityResolver;
        }
        else if (options.CreateIdentityResolver)
        {
            IdentityResolver = new IdentityResolver(httpClient);
        }

        _ownsTokenProvider = ownsTokenProvider;
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

        var url = _core.BuildUrl(BaseUrl, nsid, parameters);
        using var request = _core.CreateGetRequest(url, proxyServiceDid: null, LabelerDids);
        await AddAuthHeaderAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        return await _core.ProcessResponseAsync<TOutput>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TOutput> GetAsync<TOutput>(
        string nsid,
        string proxyServiceDid,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var url = _core.BuildUrl(BaseUrl, nsid, parameters);
        using var request = _core.CreateGetRequest(url, proxyServiceDid, LabelerDids);
        await AddAuthHeaderAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        return await _core.ProcessResponseAsync<TOutput>(response, cancellationToken).ConfigureAwait(false);
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

        var url = _core.BuildUrl(BaseUrl, nsid);
        using var request = _core.CreatePostRequest(url, input, proxyServiceDid: null, LabelerDids);
        await AddAuthHeaderAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        return await _core.ProcessResponseAsync<TOutput>(response, cancellationToken).ConfigureAwait(false);
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

        var url = _core.BuildUrl(BaseUrl, nsid);
        using var request = _core.CreatePostRequest(url, input, proxyServiceDid, LabelerDids);
        await AddAuthHeaderAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        return await _core.ProcessResponseAsync<TOutput>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TMessage> SubscribeAsync<TMessage>(
        string nsid,
        IReadOnlyDictionary<string, string>? parameters = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await foreach (var message in _core.SubscribeAsync<TMessage>(BaseUrl, nsid, parameters, cancellationToken).ConfigureAwait(false))
        {
            yield return message;
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
        var response = await _core.HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Retry on 401 if auto-retry is enabled and we have a token provider
        if (_autoRetryOnAuthFailure &&
            response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            _tokenProvider != null)
        {
            try
            {
                await _tokenProvider.RefreshAsync(cancellationToken).ConfigureAwait(false);

                // Create a new request and retry
                using var retryRequest = ATProtoClientCore.CloneRequest(request, "Authorization");
                await AddAuthHeaderAsync(retryRequest, cancellationToken).ConfigureAwait(false);

                response.Dispose();
                return await _core.HttpClient.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
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
            _core.HttpClient.Dispose();

            // Also dispose identity resolver if we created it
            IdentityResolver?.Dispose();
        }
    }
}
