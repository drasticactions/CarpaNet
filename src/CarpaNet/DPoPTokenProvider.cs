using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using CarpaNet.Auth;

namespace CarpaNet.OAuth;

/// <summary>
/// Token provider that uses OAuth 2.0 with DPoP for ATProtocol.
/// </summary>
public sealed class DPoPTokenProvider : ITokenProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IOAuthSessionStore _sessionStore;
    private readonly AuthorizationServerDiscovery _discovery;
    private readonly DPoPNonceCache _nonceCache;
    private readonly TimeSpan _refreshBuffer;
    private readonly string? _clientId;
    private readonly string? _redirectUri;
    private readonly string? _scope;
    private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

    private string? _sub;
    private DPoPKeyPair? _dpopKey;
    private TokenSet? _tokenSet;
    private OAuthAuthorizationServerMetadata? _serverMetadata;
    private bool _disposed;

    /// <inheritdoc/>
    public bool HasValidToken => _tokenSet != null && !_tokenSet.IsExpired(_refreshBuffer);

    /// <inheritdoc/>
    public string? CurrentDid => _sub;

    /// <inheritdoc/>
    public Uri? PdsUrl => _tokenSet != null ? new Uri(_tokenSet.Audience) : null;

    /// <summary>
    /// Gets the current access token.
    /// </summary>
    public string? AccessToken => _tokenSet?.AccessToken;

    /// <summary>
    /// Gets the current refresh token.
    /// </summary>
    public string? RefreshToken => _tokenSet?.RefreshToken;

    /// <summary>
    /// Gets when the current access token expires, if known.
    /// </summary>
    public DateTimeOffset? ExpiresAt => _tokenSet?.ExpiresAt;

    /// <inheritdoc/>
    public event EventHandler<TokenRefreshedEventArgs>? TokenRefreshed;

    /// <summary>
    /// Creates a new DPoP token provider.
    /// </summary>
    /// <param name="httpClient">The HttpClient to use.</param>
    /// <param name="sessionStore">The session store.</param>
    /// <param name="discovery">The authorization server discovery client.</param>
    /// <param name="refreshBuffer">Token refresh buffer time.</param>
    /// <param name="clientId">The OAuth client ID to include in token requests.</param>
    /// <param name="redirectUri">The OAuth redirect URI to persist with the session.</param>
    /// <param name="scope">The OAuth scope to persist with the session.</param>
    public DPoPTokenProvider(
        HttpClient httpClient,
        IOAuthSessionStore sessionStore,
        AuthorizationServerDiscovery? discovery = null,
        TimeSpan? refreshBuffer = null,
        string? clientId = null,
        string? redirectUri = null,
        string? scope = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _discovery = discovery ?? new AuthorizationServerDiscovery(httpClient);
        _nonceCache = new DPoPNonceCache();
        _refreshBuffer = refreshBuffer ?? TimeSpan.FromSeconds(30);
        _clientId = clientId;
        _redirectUri = redirectUri;
        _scope = scope;
    }

    /// <summary>
    /// Restores a session from stored data.
    /// </summary>
    /// <param name="sub">The user's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the session was restored, false if not found.</returns>
    public async Task<bool> RestoreSessionAsync(string sub, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var sessionData = await _sessionStore.GetAsync(sub, cancellationToken).ConfigureAwait(false);
        if (sessionData == null)
        {
            return false;
        }

        _sub = sub;
        _tokenSet = sessionData.TokenSet;
        _dpopKey = DPoPKeyPair.Import(sessionData.DPoPKey);

        // Fetch server metadata
        _serverMetadata = await _discovery.GetMetadataAsync(
            sessionData.TokenSet.Issuer,
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Sets up the provider with a token set from a successful authorization.
    /// </summary>
    internal async Task SetupAsync(
        string sub,
        TokenSet tokenSet,
        DPoPKeyPair dpopKey,
        OAuthAuthorizationServerMetadata serverMetadata,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        _sub = sub;
        _tokenSet = tokenSet;
        _dpopKey = dpopKey;
        _serverMetadata = serverMetadata;

        // Store the session
        await _sessionStore.StoreAsync(sub, new OAuthSessionData
        {
            DPoPKey = dpopKey.ExportKeyPair(),
            TokenSet = tokenSet,
            ClientId = _clientId,
            RedirectUri = _redirectUri,
            Scope = _scope
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_tokenSet == null)
        {
            return null;
        }

        // Check if we need to refresh
        if (_tokenSet.IsExpired(_refreshBuffer))
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return _tokenSet?.AccessToken;
    }

    /// <inheritdoc/>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_tokenSet == null || string.IsNullOrEmpty(_tokenSet.RefreshToken))
        {
            throw new InvalidOperationException("No refresh token available.");
        }

        if (_dpopKey == null || _serverMetadata == null)
        {
            throw new InvalidOperationException("Session not properly initialized.");
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (HasValidToken)
            {
                return;
            }

            var tokenEndpoint = _serverMetadata.TokenEndpoint;

            // Build refresh request
            var formContent = new StringBuilder();
            formContent.Append("grant_type=refresh_token");
            formContent.Append("&refresh_token=");
            formContent.Append(Uri.EscapeDataString(_tokenSet.RefreshToken));
            if (!string.IsNullOrEmpty(_clientId))
            {
                formContent.Append("&client_id=");
                formContent.Append(Uri.EscapeDataString(_clientId));
            }

            // Try with cached nonce first, retry with new nonce if needed
            var newTokenSet = await ExecuteTokenRequestWithRetryAsync(
                tokenEndpoint,
                formContent.ToString(),
                cancellationToken).ConfigureAwait(false);

            // Update token set
            var oldRefreshToken = _tokenSet.RefreshToken;
            _tokenSet = newTokenSet;
            _tokenSet.Issuer = _serverMetadata.Issuer;
            _tokenSet.Audience = PdsUrl?.ToString() ?? string.Empty;

            // Keep old refresh token if new one not provided
            if (string.IsNullOrEmpty(_tokenSet.RefreshToken))
            {
                _tokenSet.RefreshToken = oldRefreshToken;
            }

            // Update session store
            await _sessionStore.StoreAsync(_sub!, new OAuthSessionData
            {
                DPoPKey = _dpopKey.ExportKeyPair(),
                TokenSet = _tokenSet,
                ClientId = _clientId,
                RedirectUri = _redirectUri,
                Scope = _scope
            }, cancellationToken).ConfigureAwait(false);

            // Notify listeners
            TokenRefreshed?.Invoke(this, new TokenRefreshedEventArgs(
                _tokenSet.AccessToken,
                _tokenSet.RefreshToken!,
                _sub ?? string.Empty,
                null)); // OAuth doesn't return handle
        }
        catch (Exception ex)
        {
            throw new TokenRefreshException(
                "refresh_failed",
                ex.Message,
                _sub,
                ex);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Creates a DPoP-signed HTTP request.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">The request URL.</param>
    /// <param name="includeAccessToken">Whether to include the access token.</param>
    /// <returns>The HTTP request message with DPoP headers.</returns>
    public HttpRequestMessage CreateDPoPRequest(HttpMethod method, string url, bool includeAccessToken = true)
    {
        ThrowIfDisposed();

        if (_dpopKey == null)
        {
            throw new InvalidOperationException("No DPoP key available.");
        }

        var nonce = _nonceCache.Get(url);
        var accessToken = includeAccessToken ? _tokenSet?.AccessToken : null;
        var proof = _dpopKey.CreateProof(method.Method, url, nonce, accessToken);

        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("DPoP", proof);

        if (includeAccessToken && !string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", accessToken);
        }

        return request;
    }

    /// <summary>
    /// Updates the DPoP nonce from a response.
    /// </summary>
    /// <param name="response">The HTTP response.</param>
    /// <param name="url">The request URL.</param>
    public void UpdateNonceFromResponse(HttpResponseMessage response, string url)
    {
        if (response.Headers.TryGetValues("DPoP-Nonce", out var values))
        {
            foreach (var nonce in values)
            {
                _nonceCache.Set(url, nonce);
                break;
            }
        }
    }

    private async Task<TokenSet> ExecuteTokenRequestWithRetryAsync(
        string tokenEndpoint,
        string formContent,
        CancellationToken cancellationToken)
    {
        // First attempt
        try
        {
            return await ExecuteTokenRequestAsync(tokenEndpoint, formContent, cancellationToken).ConfigureAwait(false);
        }
        catch (DPoPNonceException ex) when (!string.IsNullOrEmpty(ex.NewNonce))
        {
            // Store the new nonce and retry
            _nonceCache.Set(tokenEndpoint, ex.NewNonce!);
            return await ExecuteTokenRequestAsync(tokenEndpoint, formContent, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<TokenSet> ExecuteTokenRequestAsync(
        string tokenEndpoint,
        string formContent,
        CancellationToken cancellationToken)
    {
        using var request = CreateDPoPRequest(HttpMethod.Post, tokenEndpoint, includeAccessToken: false);
        request.Content = new StringContent(formContent, Encoding.UTF8, "application/x-www-form-urlencoded");

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Always update nonce from response
        UpdateNonceFromResponse(response, tokenEndpoint);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Check for use_dpop_nonce error
            try
            {
                var errorResponse = JsonSerializer.Deserialize(errorContent, OAuthJsonContext.Default.OAuthErrorResponse);
                if (errorResponse?.Error == "use_dpop_nonce")
                {
                    // Get the nonce from the response header
                    string? newNonce = null;
                    if (response.Headers.TryGetValues("DPoP-Nonce", out var nonceValues))
                    {
                        foreach (var n in nonceValues)
                        {
                            newNonce = n;
                            break;
                        }
                    }

                    throw new DPoPNonceException(newNonce);
                }

                throw new OAuthException(
                    errorResponse?.Error ?? "token_error",
                    errorResponse?.ErrorDescription ?? errorContent);
            }
            catch (JsonException)
            {
                throw new OAuthException("token_error", errorContent);
            }
        }

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var tokenResponse = JsonSerializer.Deserialize(content, OAuthJsonContext.Default.OAuthTokenResponse);

        if (tokenResponse == null)
        {
            throw new OAuthException("invalid_response", "Failed to parse token response.");
        }

        return TokenSet.FromResponse(tokenResponse, _serverMetadata!.Issuer, _tokenSet!.Audience);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DPoPTokenProvider));
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
        _refreshLock.Dispose();
        _dpopKey?.Dispose();
    }
}
