using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet;
using CarpaNet.Auth;
using CarpaNet.Identity;

namespace CarpaNet.OAuth;

/// <summary>
/// Represents an authenticated OAuth session.
/// </summary>
public sealed class OAuthSession : IATProtoClient, IDisposable
{
    private readonly DPoPTokenProvider _tokenProvider;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IdentityResolver? _identityResolver;
    private bool _disposed;

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

    /// <summary>
    /// Gets the identity resolver for handle/DID resolution.
    /// </summary>
    public IdentityResolver? IdentityResolver => _identityResolver;

    internal OAuthSession(
        string did,
        string pdsUrl,
        DPoPTokenProvider tokenProvider,
        string? appState,
        JsonSerializerOptions? jsonOptions = null)
    {
        Did = did ?? throw new ArgumentNullException(nameof(did));
        BaseUrl = new Uri(pdsUrl);
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        AppState = appState;
        _httpClient = new HttpClient();
        _identityResolver = new IdentityResolver(_httpClient);
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

        var url = BuildUrl(nsid, parameters);
        using var request = _tokenProvider.CreateDPoPRequest(HttpMethod.Get, url);

        var response = await SendWithRetryAsync(request, url, cancellationToken).ConfigureAwait(false);
        return await ProcessResponseAsync<TOutput>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TOutput> GetAsync<TOutput>(
        string nsid,
        string proxyServiceDid,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var url = BuildUrl(nsid, parameters);
        using var request = _tokenProvider.CreateDPoPRequest(HttpMethod.Get, url);
        request.Headers.Add("atproto-proxy", proxyServiceDid);

        var response = await SendWithRetryAsync(request, url, cancellationToken).ConfigureAwait(false);
        return await ProcessResponseAsync<TOutput>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        TInput? input,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var url = BuildUrl(nsid);
        using var request = _tokenProvider.CreateDPoPRequest(HttpMethod.Post, url);

        if (input != null)
        {
            var typeInfo = (JsonTypeInfo<TInput>)_jsonOptions.GetTypeInfo(typeof(TInput));
            var json = JsonSerializer.Serialize(input, typeInfo);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        var response = await SendWithRetryAsync(request, url, cancellationToken).ConfigureAwait(false);
        return await ProcessResponseAsync<TOutput>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        string proxyServiceDid,
        TInput? input,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var url = BuildUrl(nsid);
        using var request = _tokenProvider.CreateDPoPRequest(HttpMethod.Post, url);
        request.Headers.Add("atproto-proxy", proxyServiceDid);

        if (input != null)
        {
            var typeInfo = (JsonTypeInfo<TInput>)_jsonOptions.GetTypeInfo(typeof(TInput));
            var json = JsonSerializer.Serialize(input, typeInfo);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        var response = await SendWithRetryAsync(request, url, cancellationToken).ConfigureAwait(false);
        return await ProcessResponseAsync<TOutput>(response, cancellationToken).ConfigureAwait(false);
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
    public void SignOut()
    {
        // The actual revocation should be done through ATProtoOAuthClient.RevokeAsync
        // This just marks the local session as invalid
        Dispose();
    }

    private string BuildUrl(string nsid, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var url = $"{BaseUrl.ToString().TrimEnd('/')}/xrpc/{nsid}";

        if (parameters != null && parameters.Count > 0)
        {
            var sb = new System.Text.StringBuilder(url);
            sb.Append('?');

            var first = true;
            foreach (var kvp in parameters)
            {
                if (!first)
                {
                    sb.Append('&');
                }
                first = false;

                sb.Append(Uri.EscapeDataString(kvp.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(kvp.Value));
            }

            url = sb.ToString();
        }

        return url;
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

    private async Task<TOutput> ProcessResponseAsync<TOutput>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new OAuthException(
                "request_failed",
                $"Request failed with status {response.StatusCode}: {errorContent}");
        }

#if NET8_0_OR_GREATER
        var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var typeInfo = (JsonTypeInfo<TOutput>)_jsonOptions.GetTypeInfo(typeof(TOutput));
        var result = await JsonSerializer.DeserializeAsync(content, typeInfo, cancellationToken).ConfigureAwait(false);
#else
        var contentString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var typeInfo = (JsonTypeInfo<TOutput>)_jsonOptions.GetTypeInfo(typeof(TOutput));
        var result = JsonSerializer.Deserialize<TOutput>(contentString, typeInfo);
#endif

        if (result == null)
        {
            throw new OAuthException("invalid_response", "Failed to deserialize response.");
        }

        return result;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OAuthSession));
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
