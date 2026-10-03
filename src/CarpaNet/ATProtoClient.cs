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
using CarpaNet.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarpaNet;

/// <summary>
/// Default Implementation of <see cref="IATProtoClient"/>.
/// </summary>
public sealed class ATProtoClient : IATProtoClient, IXrpcRequestClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CborSerializerContext _cborContext;
    private readonly bool _ownsHttpClient;
    private readonly ITokenProvider? _tokenProvider;
    private readonly bool _autoRetryOnAuthFailure;
    private readonly Uri? _configuredBaseUrl;
    private readonly ILogger<ATProtoClient> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string? _userAgent;
    private IReadOnlyList<string>? _labelerDids;
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
    /// Gets the labeler DIDs sent in the <c>atproto-accept-labelers</c> header.
    /// Change it with <see cref="SetLabelerDids(IEnumerable{string}?)"/>.
    /// </summary>
    public IReadOnlyList<string>? LabelerDids => Volatile.Read(ref _labelerDids);

    /// <inheritdoc/>
    public JsonSerializerOptions JsonOptions => _jsonOptions;

    /// <summary>
    /// Replaces the labeler DIDs sent in the <c>atproto-accept-labelers</c> header on later requests.
    /// Entries may carry parameters such as <c>;redact</c> (see <see cref="AcceptLabelersHeader.Redact(string)"/>).
    /// </summary>
    /// <param name="labelerDids">The labeler DIDs, or null to send no header.</param>
    public void SetLabelerDids(IEnumerable<string>? labelerDids)
    {
        Volatile.Write(ref _labelerDids, labelerDids?.ToArray());
    }

    /// <summary>
    /// Gets the token provider, if any.
    /// </summary>
    public ITokenProvider? TokenProvider => _tokenProvider;

    /// <inheritdoc/>
    public HttpClient HttpClient => _httpClient;

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
        => LoginAsync(identifier, password, serviceUrl, authFactorToken, allowTakendown: false, cancellationToken);

    /// <summary>
    /// Authenticates with the ATProtocol service using credentials.
    /// Only works when the client was created with a <see cref="SessionTokenProvider"/>.
    /// </summary>
    /// <param name="identifier">The user identifier (handle, email, or DID).</param>
    /// <param name="password">The password or App Password.</param>
    /// <param name="serviceUrl">The service URL (default: Bluesky Entryway).</param>
    /// <param name="authFactorToken">Optional 2FA token.</param>
    /// <param name="allowTakendown">When true, a takendown account can sign in and see its status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session response with user info.</returns>
    public async Task<SessionResponse> LoginAsync(
        string identifier,
        string password,
        Uri? serviceUrl,
        string? authFactorToken,
        bool allowTakendown,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var sessionProvider = GetSessionTokenProvider();
        var response = await sessionProvider.LoginAsync(identifier, password, serviceUrl, authFactorToken, allowTakendown, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Logged in as {Did}", response.Did);
        return response;
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
        _logger.LogInformation("Session restored for {Did}", did);
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
                    await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore errors when deleting session
            }
        }

        await sessionProvider.ClearSessionAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Logged out");
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
        var httpClient = options.HttpClient ?? CreateOwnedHttpClient(options);
        var ownsHttpClient = options.HttpClient == null;

        // Create session token provider and login
        var tokenProvider = new SessionTokenProvider(httpClient, sessionStore: options.SessionStore, loggerFactory: options.LoggerFactory);
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

        var httpClient = options.HttpClient ?? CreateOwnedHttpClient(options);
        var ownsHttpClient = options.HttpClient == null;

        var tokenProvider = new SessionTokenProvider(httpClient, sessionStore: options.SessionStore, loggerFactory: options.LoggerFactory);
        tokenProvider.RestoreSession(accessJwt, refreshJwt, did, handle, pdsUrl);

        options.BaseUrl = pdsUrl;
        options.TokenProvider = tokenProvider;
        options.HttpClient = httpClient;

        return new ATProtoClient(options, ownsHttpClient, ownsTokenProvider: true);
    }

    /// <summary>
    /// Creates an ATProtoClient. By default, creates a public (unauthenticated) client using the Bluesky public AppView.
    /// </summary>
    /// <param name="options">Optional configuration options.</param>
    /// <returns>An ATProtoClient instance.</returns>
    public static ATProtoClient Create(ATProtoClientOptions? options = null)
    {
        options ??= new ATProtoClientOptions();
        options = options.Clone();

        options.BaseUrl ??= new Uri(BlueskyServices.PublicAppView);

        var httpClient = options.HttpClient ?? CreateOwnedHttpClient(options);
        var ownsHttpClient = options.HttpClient == null;

        var tokenProvider = new SessionTokenProvider(httpClient, sessionStore: options.SessionStore, loggerFactory: options.LoggerFactory);
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

        _loggerFactory = options.LoggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<ATProtoClient>();

        var httpClient = options.HttpClient ?? CreateOwnedHttpClient(options);
        _ownsHttpClient = ownsHttpClient;
        _userAgent = options.UserAgent;

        var jsonOptions = options.JsonOptions ?? throw new ArgumentException("JsonOptions must be provided.", nameof(options));
        var cborContext = options.CborContext ?? throw new ArgumentException("CborContext must be provided.", nameof(options));

        _httpClient = httpClient;
        _jsonOptions = jsonOptions;
        _cborContext = cborContext;
        _tokenProvider = options.TokenProvider;
        _autoRetryOnAuthFailure = options.AutoRetryOnAuthFailure;
        _labelerDids = options.LabelerDids?.ToArray();

        // Store configured base URL (null means dynamic - will use token provider's PDS URL)
        _configuredBaseUrl = options.BaseUrl ?? _tokenProvider?.PdsUrl;

        // Create or use identity resolver
        if (options.IdentityResolver != null)
        {
            IdentityResolver = options.IdentityResolver;
        }
        else if (options.CreateIdentityResolver)
        {
            IdentityResolver = new IdentityResolver(httpClient, cache: new MemoryIdentityCache(), loggerFactory: _loggerFactory);
        }

        _ownsTokenProvider = ownsTokenProvider;
    }

    private readonly bool _ownsTokenProvider;

    #endregion

    #region IATProtoClient Implementation

    /// <inheritdoc/>
    public Task<TOutput> GetAsync<TOutput>(
        string nsid,
        IEnumerable<KeyValuePair<string, string>>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return this.QueryAsync<TOutput>(nsid, parameters, null, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TOutput> GetAsync<TOutput>(
        string nsid,
        string proxyServiceDid,
        IEnumerable<KeyValuePair<string, string>>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return this.QueryAsync<TOutput>(nsid, parameters, new XrpcRequestOptions { ProxyServiceDid = proxyServiceDid }, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        TInput? input,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNoTokenProvider();
        return this.ProcedureAsync<TInput, TOutput>(nsid, null, input, null, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        string proxyServiceDid,
        TInput? input,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNoTokenProvider();
        return this.ProcedureAsync<TInput, TOutput>(nsid, null, input, new XrpcRequestOptions { ProxyServiceDid = proxyServiceDid }, cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TMessage> SubscribeAsync<TMessage>(
        string nsid,
        IEnumerable<KeyValuePair<string, string>>? parameters = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var eventStreamClient = new EventStreamClient(BaseUrl, _cborContext, _loggerFactory);
        try
        {
            var paramList = parameters != null
                ? new List<KeyValuePair<string, string?>>(
                    parameters.Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value)))
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

    #region IXrpcRequestClient Implementation

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Requests go to the session's PDS unless <see cref="XrpcRequestOptions.ServiceUrl"/> is set.
    /// A query with a <c>repo</c> parameter and no proxy is sent to that repo's PDS when an
    /// <see cref="IdentityResolver"/> is configured.
    /// </para>
    /// <para>
    /// The access token is attached only when the request goes to the session's own PDS, so
    /// credentials are never sent to another server. A 401 from the PDS triggers one token refresh
    /// and retry when the body can be replayed.
    /// </para>
    /// </remarks>
    public async Task<HttpResponseMessage> SendXrpcAsync(XrpcRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var options = request.Options;
        var proxy = options?.EffectiveProxyServiceDid;
        var url = await ResolveRequestUrlAsync(request, proxy, cancellationToken).ConfigureAwait(false);
        var attachCredentials = ShouldAttachCredentials(url, options);

        _logger.LogDebug("Sending {Method} {Nsid}", request.Method, request.Nsid);

        var response = await SendOnceAsync(request, url, proxy, attachCredentials, isRetry: false, cancellationToken).ConfigureAwait(false);

        if (_autoRetryOnAuthFailure &&
            attachCredentials &&
            response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            (request.Body == null || request.Body.IsReplayable))
        {
            _logger.LogWarning("Received 401, refreshing token and retrying");
            try
            {
                await _tokenProvider!.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ATProtoException)
            {
                // Includes AuthenticationException and the 400 a rejected refresh token gets.
                _logger.LogWarning("Token refresh failed, returning 401");
                return response;
            }
            catch (InvalidOperationException)
            {
                _logger.LogWarning("Token refresh failed, returning 401");
                return response;
            }

            response.Dispose();
            response = await SendOnceAsync(request, url, proxy, attachCredentials, isRetry: true, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    #endregion

    #region Private Methods

    private static HttpClient CreateOwnedHttpClient(ATProtoClientOptions options)
    {
        return HttpClientFactory.Create(new HttpClientFactoryOptions
        {
            Timeout = options.Timeout,
            UserAgent = options.UserAgent,
            EnableRateLimitHandler = options.EnableRateLimitHandler,
            AutoRetryOnRateLimit = options.AutoRetryOnRateLimit,
            RateLimitMaxRetries = options.RateLimitMaxRetries,
            LoggerFactory = options.LoggerFactory,
        });
    }

    private void ThrowIfNoTokenProvider()
    {
        if (_tokenProvider == null)
        {
            throw new InvalidOperationException(
                "Cannot make POST requests without authentication. " +
                "Use CreateWithSessionAsync or provide a TokenProvider.");
        }
    }

    private async Task<Uri> ResolveRequestUrlAsync(XrpcRequest request, string? proxy, CancellationToken cancellationToken)
    {
        var serviceUrl = request.Options?.ServiceUrl;
        if (serviceUrl != null)
        {
            return XrpcHttpHandler.BuildUrl(serviceUrl, request.Nsid, request.Parameters);
        }

        var baseUrl = _tokenProvider?.PdsUrl ?? BaseUrl;
        if (request.Method == HttpMethod.Get && proxy == null)
        {
            return await XrpcHttpHandler.BuildUrlAsync(
                baseUrl, request.Nsid, request.Parameters,
                IdentityResolver, _logger, cancellationToken).ConfigureAwait(false);
        }

        return XrpcHttpHandler.BuildUrl(baseUrl, request.Nsid, request.Parameters);
    }

    private bool ShouldAttachCredentials(Uri url, XrpcRequestOptions? options)
    {
        if (_tokenProvider == null || options?.ServiceUrl != null || HasAuthorizationHeader(options))
        {
            return false;
        }

        var credentialOrigin = _tokenProvider.PdsUrl ?? _configuredBaseUrl;
        return credentialOrigin != null && XrpcHttpHandler.IsSameOrigin(url, credentialOrigin);
    }

    private static bool HasAuthorizationHeader(XrpcRequestOptions? options)
    {
        if (options?.Headers == null)
        {
            return false;
        }

        foreach (var header in options.Headers)
        {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        XrpcRequest request,
        Uri url,
        string? proxy,
        bool attachCredentials,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(request.Method, url);
        XrpcHttpHandler.AddCommonHeaders(message, proxy, request.Options?.AcceptLabelers ?? LabelerDids);
        XrpcHttpHandler.AddCustomHeaders(message, request.Options?.Headers);

        if (_userAgent != null && message.Headers.UserAgent.Count == 0 && _httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            message.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        }

        if (request.Body != null)
        {
            message.Content = request.Body.CreateContent()
                ?? throw new InvalidOperationException("The request body has already been sent and cannot be replayed.");
        }

        if (attachCredentials)
        {
            var token = await _tokenProvider!.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        if (isRetry)
        {
            _logger.LogDebug("Retrying {Method} {Nsid}", request.Method, request.Nsid);
        }

        return await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
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
