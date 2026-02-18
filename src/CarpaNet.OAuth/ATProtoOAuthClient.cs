using System;
using System.Collections.Generic;
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
public sealed class ATProtoOAuthClient : IATProtoClient, IDisposable
{
    private readonly DPoPTokenProvider _tokenProvider;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IdentityResolver? _identityResolver;
    private readonly ILogger<ATProtoOAuthClient> _logger;
    private bool _disposed;
    private readonly OAuthSession _session;

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
    /// Gets the list of labeler DIDs whose labels should be included in responses.
    /// </summary>
    public IReadOnlyList<string>? LabelerDids { get; }

    internal ATProtoOAuthClient(
        string did,
        string pdsUrl,
        DPoPTokenProvider tokenProvider,
        OAuthSession session,
        string? appState,
        IdentityResolver identityResolver,
        JsonSerializerOptions? jsonOptions = null,
        IReadOnlyList<string>? labelerDids = null,
        ILoggerFactory? loggerFactory = null)
    {
        Did = did ?? throw new ArgumentNullException(nameof(did));
        BaseUrl = new Uri(pdsUrl);
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        AppState = appState;
        LabelerDids = labelerDids;
        _httpClient = new HttpClient();
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
    public async Task<TOutput> GetAsync<TOutput>(
        string nsid,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _logger.LogDebug("OAuth GET {Nsid}", nsid);

        var url = (await XrpcHttpHandler.BuildUrlAsync(BaseUrl, nsid, parameters, _identityResolver, cancellationToken).ConfigureAwait(false)).ToString();
        using var request = _tokenProvider.CreateDPoPRequest(HttpMethod.Get, url);
        XrpcHttpHandler.AddCommonHeaders(request, null, LabelerDids);

        var response = await SendWithRetryAsync(request, url, cancellationToken).ConfigureAwait(false);
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

        var url = (await XrpcHttpHandler.BuildUrlAsync(BaseUrl, nsid, parameters, _identityResolver, cancellationToken).ConfigureAwait(false)).ToString();
        using var request = _tokenProvider.CreateDPoPRequest(HttpMethod.Get, url);
        XrpcHttpHandler.AddCommonHeaders(request, proxyServiceDid, LabelerDids);

        var response = await SendWithRetryAsync(request, url, cancellationToken).ConfigureAwait(false);
        return await XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        TInput? input,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _logger.LogDebug("OAuth POST {Nsid}", nsid);

        var url = XrpcHttpHandler.BuildUrl(BaseUrl, nsid).ToString();
        using var request = _tokenProvider.CreateDPoPRequest(HttpMethod.Post, url);
        XrpcHttpHandler.AddCommonHeaders(request, null, LabelerDids);

        if (input != null)
        {
            var typeInfo = (JsonTypeInfo<TInput>)_jsonOptions.GetTypeInfo(typeof(TInput));
            var json = JsonSerializer.Serialize(input, typeInfo);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        var response = await SendWithRetryAsync(request, url, cancellationToken).ConfigureAwait(false);
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

        var url = XrpcHttpHandler.BuildUrl(BaseUrl, nsid).ToString();
        using var request = _tokenProvider.CreateDPoPRequest(HttpMethod.Post, url);
        XrpcHttpHandler.AddCommonHeaders(request, proxyServiceDid, LabelerDids);

        if (input != null)
        {
            var typeInfo = (JsonTypeInfo<TInput>)_jsonOptions.GetTypeInfo(typeof(TInput));
            var json = JsonSerializer.Serialize(input, typeInfo);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        var response = await SendWithRetryAsync(request, url, cancellationToken).ConfigureAwait(false);
        return await XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TMessage> SubscribeAsync<TMessage>(
        string nsid,
        IReadOnlyDictionary<string, string>? parameters = null,
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

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage request,
        string url,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _tokenProvider.UpdateNonceFromResponse(response, url);

        // Handle 401 with token refresh
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401, refreshing DPoP token and retrying");
            // Try to refresh the token
            await _tokenProvider.RefreshAsync(cancellationToken).ConfigureAwait(false);

            // Create a new request (can't reuse the old one)
            using var retryRequest = _tokenProvider.CreateDPoPRequest(request.Method, url);

            if (request.Content != null)
            {
                // Clone content
                var contentBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                retryRequest.Content = new ByteArrayContent(contentBytes);

                foreach (var header in request.Content.Headers)
                {
                    retryRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // Copy custom headers
            foreach (var header in request.Headers)
            {
                if (header.Key != "Authorization" && header.Key != "DPoP")
                {
                    retryRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            response.Dispose();
            response = await _httpClient.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
            _tokenProvider.UpdateNonceFromResponse(response, url);
        }

        return response;
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
        _identityResolver?.Dispose();
        _httpClient.Dispose();
    }
}
