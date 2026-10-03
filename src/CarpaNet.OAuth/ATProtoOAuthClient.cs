using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet;
using CarpaNet.Auth;
using CarpaNet.Http;
using CarpaNet.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarpaNet.OAuth;

/// <summary>
/// Represents an authenticated OAuth session.
/// </summary>
public sealed class ATProtoOAuthClient : IATProtoClient, IXrpcRequestClient, IDisposable
{
    private readonly DPoPTokenProvider _tokenProvider;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IdentityResolver? _identityResolver;
    private readonly ILogger<ATProtoOAuthClient> _logger;
    private bool _disposed;
    private readonly OAuthSession _session;
    private IReadOnlyList<string>? _labelerDids;

    /// <summary>
    /// Gets the user's DID.
    /// </summary>
    public string Did { get; }

    /// <summary>
    /// Gets the PDS URL.
    /// </summary>
    public Uri BaseUrl { get; }

    /// <summary>
    /// Gets the application state that was passed to the authorize call.
    /// </summary>
    public string? AppState { get; }

    /// <summary>
    /// Gets whether the session is authenticated.
    /// </summary>
    public bool IsAuthenticated => _tokenProvider.HasValidToken;

    /// <summary>
    /// Gets the authenticated DID.
    /// </summary>
    public string? AuthenticatedDid => Did;

    /// <summary>
    /// Gets the token provider for this session.
    /// </summary>
    public ITokenProvider TokenProvider => _tokenProvider;

    /// <inheritdoc/>
    public HttpClient HttpClient => _httpClient;

    /// <summary>
    /// Gets the identity resolver for handle/DID resolution.
    /// </summary>
    public IdentityResolver? IdentityResolver => _identityResolver;

    /// <summary>
    /// Gets the labeler DIDs sent in the <c>atproto-accept-labelers</c> header.
    /// Change it with <see cref="SetLabelerDids(IEnumerable{string}?)"/>.
    /// </summary>
    public IReadOnlyList<string>? LabelerDids => Volatile.Read(ref _labelerDids);

    /// <inheritdoc/>
    public JsonSerializerOptions JsonOptions => _jsonOptions;

    /// <summary>
    /// Replaces the labeler DIDs sent in the <c>atproto-accept-labelers</c> header on later requests.
    /// Entries may carry parameters such as <c>;redact</c>.
    /// </summary>
    /// <param name="labelerDids">The labeler DIDs, or null to send no header.</param>
    public void SetLabelerDids(IEnumerable<string>? labelerDids)
    {
        Volatile.Write(ref _labelerDids, labelerDids?.ToArray());
    }

    internal ATProtoOAuthClient(
        string did,
        string pdsUrl,
        DPoPTokenProvider tokenProvider,
        OAuthSession session,
        string? appState,
        IdentityResolver identityResolver,
        JsonSerializerOptions? jsonOptions = null,
        IReadOnlyList<string>? labelerDids = null,
        ILoggerFactory? loggerFactory = null,
        HttpClient? httpClient = null)
    {
        Did = did ?? throw new ArgumentNullException(nameof(did));
        BaseUrl = new Uri(pdsUrl);
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        AppState = appState;
        _labelerDids = labelerDids?.ToArray();
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient == null;
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = factory.CreateLogger<ATProtoOAuthClient>();
        _identityResolver = identityResolver;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

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
        return this.ProcedureAsync<TInput, TOutput>(nsid, null, input, new XrpcRequestOptions { ProxyServiceDid = proxyServiceDid }, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Requests go to the session's PDS unless <see cref="XrpcRequestOptions.ServiceUrl"/> is set.
    /// The DPoP-bound access token and proof are attached only when the request goes to the
    /// session's own PDS, so credentials are never sent to another server.
    /// </para>
    /// <para>
    /// A 401 from the PDS is retried once with a fresh nonce when the server asks for one
    /// (<c>use_dpop_nonce</c>), and otherwise once after a token refresh, when the body can be replayed.
    /// </para>
    /// </remarks>
    public async Task<HttpResponseMessage> SendXrpcAsync(XrpcRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var options = request.Options;
        var proxy = options?.EffectiveProxyServiceDid;
        var url = await ResolveRequestUrlAsync(request, proxy, cancellationToken).ConfigureAwait(false);
        var urlString = url.ToString();
        var attachCredentials = options?.ServiceUrl == null
            && !HasAuthorizationHeader(options)
            && XrpcHttpHandler.IsSameOrigin(url, BaseUrl);

        _logger.LogDebug("OAuth {Method} {Nsid}", request.Method, request.Nsid);

        if (attachCredentials)
        {
            // Refreshes the token first when it is about to expire.
            await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        }

        var retriedNonce = false;
        var refreshed = false;
        while (true)
        {
            var response = await SendOnceAsync(request, url, urlString, proxy, attachCredentials, cancellationToken).ConfigureAwait(false);

            if (!attachCredentials
                || response.StatusCode != System.Net.HttpStatusCode.Unauthorized
                || (request.Body != null && !request.Body.IsReplayable))
            {
                return response;
            }

            if (!retriedNonce && IsUseDPoPNonceChallenge(response))
            {
                // The nonce from the response is already cached; send again with a new proof.
                _logger.LogDebug("DPoP nonce required, retrying with the new nonce");
                retriedNonce = true;
                response.Dispose();
                continue;
            }

            if (refreshed)
            {
                return response;
            }

            _logger.LogWarning("Received 401, refreshing DPoP token and retrying");
            try
            {
                await _tokenProvider.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TokenRefreshException || ex is InvalidOperationException)
            {
                _logger.LogWarning("DPoP token refresh failed, returning 401");
                return response;
            }

            refreshed = true;
            response.Dispose();
        }
    }

    private async Task<Uri> ResolveRequestUrlAsync(XrpcRequest request, string? proxy, CancellationToken cancellationToken)
    {
        var serviceUrl = request.Options?.ServiceUrl;
        if (serviceUrl != null)
        {
            return XrpcHttpHandler.BuildUrl(serviceUrl, request.Nsid, request.Parameters);
        }

        if (request.Method == HttpMethod.Get && proxy == null)
        {
            return await XrpcHttpHandler.BuildUrlAsync(
                BaseUrl, request.Nsid, request.Parameters,
                _identityResolver, _logger, cancellationToken).ConfigureAwait(false);
        }

        return XrpcHttpHandler.BuildUrl(BaseUrl, request.Nsid, request.Parameters);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        XrpcRequest request,
        Uri url,
        string urlString,
        string? proxy,
        bool attachCredentials,
        CancellationToken cancellationToken)
    {
        using var message = attachCredentials
            ? await _tokenProvider.CreateDPoPRequestAsync(request.Method, urlString).ConfigureAwait(false)
            : new HttpRequestMessage(request.Method, url);

        XrpcHttpHandler.AddCommonHeaders(message, proxy, request.Options?.AcceptLabelers ?? LabelerDids);
        XrpcHttpHandler.AddCustomHeaders(message, request.Options?.Headers);

        if (attachCredentials)
        {
            // A DPoP proof is single use; if RateLimitHandler retries this request it must sign a new one.
            RateLimitHandler.SetRetryPreparer(message, retry => _tokenProvider.AddDPoPHeadersAsync(retry));
        }

        if (request.Body != null)
        {
            message.Content = request.Body.CreateContent()
                ?? throw new InvalidOperationException("The request body has already been sent and cannot be replayed.");
        }

        var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (attachCredentials)
        {
            _tokenProvider.UpdateNonceFromResponse(response, urlString);
        }

        return response;
    }

    private static bool IsUseDPoPNonceChallenge(HttpResponseMessage response)
    {
        foreach (var challenge in response.Headers.WwwAuthenticate)
        {
            if (string.Equals(challenge.Scheme, "DPoP", StringComparison.OrdinalIgnoreCase)
                && challenge.Parameter != null
                && challenge.Parameter.IndexOf("use_dpop_nonce", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
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

    /// <inheritdoc/>
    public IAsyncEnumerable<TMessage> SubscribeAsync<TMessage>(
        string nsid,
        IEnumerable<KeyValuePair<string, string>>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // For now, subscriptions don't support OAuth
        // The EventStreamClient would need DPoP support
        throw new NotSupportedException("Subscriptions are not yet supported with OAuth sessions.");
    }

    /// <summary>
    /// Signs out of the session.
    /// </summary>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _logger.LogInformation("Signing out OAuth session");
        await _session.RevokeAsync(Did, cancellationToken).ConfigureAwait(false);
        Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ATProtoOAuthClient));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tokenProvider.Dispose();

        // The identity resolver belongs to the OAuthSession that created this client and may be
        // shared with other clients, so it is not disposed here.
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
