using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using CarpaNet;
using CarpaNet.Identity;

namespace CarpaNet.OAuth;

/// <summary>
/// OAuth 2.0 client for ATProtocol with DPoP, PAR, and PKCE support.
/// </summary>
public sealed class OAuthSession : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly OAuthClientConfig _config;
    private readonly IOAuthStateStore _stateStore;
    private readonly IOAuthSessionStore _sessionStore;
    private readonly AuthorizationServerDiscovery _discovery;
    private readonly IdentityResolver? _identityResolver;
    private bool _disposed;

    /// <summary>
    /// Creates a new ATProto OAuth client.
    /// </summary>
    /// <param name="config">The OAuth client configuration.</param>
    public OAuthSession(OAuthClientConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        _httpClient = config.HttpClient ?? new HttpClient();
        _ownsHttpClient = config.HttpClient == null;

        _stateStore = config.StateStore ?? new MemoryOAuthStateStore();
        _sessionStore = config.SessionStore ?? new MemoryOAuthSessionStore();
        _discovery = new AuthorizationServerDiscovery(_httpClient);
        _identityResolver = new IdentityResolver(_httpClient, dnsResolver: new CarpaNet.Identity.DefaultDnsResolver());
    }

    /// <summary>
    /// Starts the OAuth authorization flow.
    /// </summary>
    /// <param name="input">The user identifier (handle, DID, or PDS URL).</param>
    /// <param name="appState">Optional application-defined state to preserve across the flow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization URL to redirect the user to.</returns>
    public async Task<string> AuthorizeAsync(
        string input,
        string? appState = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Resolve identity to find PDS and authorization server
        var (pdsUrl, issuer, serverMetadata) = await ResolveIdentityAsync(input, cancellationToken).ConfigureAwait(false);

        // Generate PKCE
        var (verifier, challenge) = Pkce.Generate();

        // Generate state
        var state = Pkce.GenerateState();

        // Generate DPoP key
        var dpopKey = DPoPKeyPair.Generate();

        // Store state data
        var stateData = new OAuthStateData
        {
            Issuer = issuer,
            DPoPKey = dpopKey.ExportKeyPair(),
            Verifier = verifier,
            AppState = appState,
            PdsUrl = pdsUrl,
            ExpiresAt = DateTimeOffset.UtcNow + _config.StateExpiration
        };

        await _stateStore.StoreAsync(state, stateData, cancellationToken).ConfigureAwait(false);

        // Build authorization request parameters
        var authParams = new Dictionary<string, string>
        {
            ["client_id"] = _config.ClientId,
            ["redirect_uri"] = _config.RedirectUri,
            ["response_type"] = "code",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = _config.Scope,
            ["dpop_jkt"] = dpopKey.Thumbprint
        };

        // Add login hint if we have a handle
        #if NETSTANDARD
        if (input.Contains(".") && !input.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
        {
            authParams["login_hint"] = input;
        }
        #else
        if (input.Contains('.') && !input.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
        {
            authParams["login_hint"] = input;
        }
        #endif

        // Try PAR if available
        if (!string.IsNullOrEmpty(serverMetadata.PushedAuthorizationRequestEndpoint))
        {
            try
            {
                var requestUri = await PushAuthorizationRequestAsync(
                    serverMetadata.PushedAuthorizationRequestEndpoint!,
                    authParams,
                    dpopKey,
                    cancellationToken).ConfigureAwait(false);

                // Build authorization URL with request_uri
                return BuildAuthorizationUrl(
                    serverMetadata.AuthorizationEndpoint,
                    new Dictionary<string, string>
                    {
                        ["client_id"] = _config.ClientId,
                        ["request_uri"] = requestUri
                    });
            }
            catch (OAuthException)
            {
                // Fall back to standard authorization URL if PAR fails
            }
        }

        // Build standard authorization URL
        return BuildAuthorizationUrl(serverMetadata.AuthorizationEndpoint, authParams);
    }

    /// <summary>
    /// Handles the OAuth callback and exchanges the code for tokens.
    /// </summary>
    /// <param name="callbackUrl">The full callback URL with query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The OAuth session.</returns>
    public async Task<ATProtoOAuthClient> CallbackAsync(
        string callbackUrl,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var uri = new Uri(callbackUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        // Check for error
        var error = query["error"];
        if (!string.IsNullOrEmpty(error))
        {
            var errorDescription = query["error_description"];
            var state = query["state"];

            // Try to get app state
            string? appState = null;
            if (!string.IsNullOrEmpty(state))
            {
                var stateData = await _stateStore.ConsumeAsync(state, cancellationToken).ConfigureAwait(false);
                appState = stateData?.AppState;
            }

            throw new OAuthCallbackException(error, errorDescription, appState);
        }

        // Get code and state
        var code = query["code"];
        var stateParam = query["state"];

        if (string.IsNullOrEmpty(code))
        {
            throw new OAuthException("missing_code", "Authorization code not found in callback.");
        }

        if (string.IsNullOrEmpty(stateParam))
        {
            throw new OAuthException("missing_state", "State parameter not found in callback.");
        }

        // Consume state (atomically retrieve and delete)
        var storedState = await _stateStore.ConsumeAsync(stateParam, cancellationToken).ConfigureAwait(false);
        if (storedState == null)
        {
            throw new OAuthException("invalid_state", "State parameter not found or expired.");
        }

        // Restore DPoP key
        var dpopKey = DPoPKeyPair.Import(storedState.DPoPKey);

        try
        {
            // Get server metadata
            var serverMetadata = await _discovery.GetMetadataAsync(
                storedState.Issuer,
                cancellationToken).ConfigureAwait(false);

            // Exchange code for tokens
            var tokenSet = await ExchangeCodeAsync(
                serverMetadata.TokenEndpoint,
                code,
                storedState.Verifier,
                dpopKey,
                cancellationToken).ConfigureAwait(false);

            tokenSet.Issuer = storedState.Issuer;
            tokenSet.Audience = storedState.PdsUrl ?? string.Empty;

            // Create token provider
            var tokenProvider = new DPoPTokenProvider(
                _httpClient,
                _sessionStore,
                _discovery,
                _config.RefreshBuffer,
                _config.ClientId,
                _config.RedirectUri,
                _config.Scope);

            await tokenProvider.SetupAsync(
                tokenSet.Sub,
                tokenSet,
                dpopKey,
                serverMetadata,
                cancellationToken).ConfigureAwait(false);

            // Create session
            return new ATProtoOAuthClient(
                tokenSet.Sub,
                tokenSet.Audience,
                tokenProvider,
                storedState.AppState,
                _config.JsonOptions);
        }
        catch
        {
            dpopKey.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Restores an existing OAuth session.
    /// </summary>
    /// <param name="sub">The user's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The restored OAuth session, or null if not found.</returns>
    public async Task<ATProtoOAuthClient?> RestoreSessionAsync(
        string sub,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var tokenProvider = new DPoPTokenProvider(
            _httpClient,
            _sessionStore,
            _discovery,
            _config.RefreshBuffer,
            _config.ClientId,
            _config.RedirectUri,
            _config.Scope);

        var restored = await tokenProvider.RestoreSessionAsync(sub, cancellationToken).ConfigureAwait(false);
        if (!restored)
        {
            tokenProvider.Dispose();
            return null;
        }

        return new ATProtoOAuthClient(
            sub,
            tokenProvider.PdsUrl?.ToString() ?? string.Empty,
            tokenProvider,
            null,
            _config.JsonOptions);
    }

    /// <summary>
    /// Revokes a session's tokens.
    /// </summary>
    /// <param name="sub">The user's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RevokeAsync(string sub, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Get session data
        var sessionData = await _sessionStore.GetAsync(sub, cancellationToken).ConfigureAwait(false);
        if (sessionData == null)
        {
            return;
        }

        // Try to revoke the token at the server
        try
        {
            var serverMetadata = await _discovery.GetMetadataAsync(
                sessionData.TokenSet.Issuer,
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(serverMetadata.RevocationEndpoint) &&
                !string.IsNullOrEmpty(sessionData.TokenSet.RefreshToken))
            {
                var dpopKey = DPoPKeyPair.Import(sessionData.DPoPKey);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, serverMetadata.RevocationEndpoint);

                    var nonce = new DPoPNonceCache().Get(serverMetadata.RevocationEndpoint!);
                    var proof = dpopKey.CreateProof("POST", serverMetadata.RevocationEndpoint!, nonce);
                    request.Headers.Add("DPoP", proof);

                    var content = $"token={Uri.EscapeDataString(sessionData.TokenSet.RefreshToken)}&token_type_hint=refresh_token";
                    request.Content = new StringContent(content, Encoding.UTF8, "application/x-www-form-urlencoded");

                    await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    dpopKey.Dispose();
                }
            }
        }
        catch
        {
            // Ignore revocation errors
        }

        // Delete the session
        await _sessionStore.DeleteAsync(sub, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string pdsUrl, string issuer, OAuthAuthorizationServerMetadata metadata)> ResolveIdentityAsync(
        string input,
        CancellationToken cancellationToken)
    {
        string pdsUrl;
        string issuer;

        // Check if input is a URL
        if (Uri.TryCreate(input, UriKind.Absolute, out var inputUri) &&
            (inputUri.Scheme == "http" || inputUri.Scheme == "https"))
        {
            pdsUrl = input.TrimEnd('/');
        }
        else if (_identityResolver != null)
        {
            // Resolve handle or DID to PDS
            var didDoc = await _identityResolver.ResolveAsync(input, cancellationToken).ConfigureAwait(false);

            pdsUrl = didDoc.PdsEndpoint
                ?? throw new OAuthException("pds_not_found", $"No PDS URL found for: {input}");
        }
        else
        {
            throw new OAuthException("identity_resolver_required", "Identity resolver required for handle/DID input.");
        }

        // Discover authorization server from PDS
        issuer = await _discovery.DiscoverAuthorizationServerAsync(pdsUrl, cancellationToken).ConfigureAwait(false);

        // Get server metadata
        var metadata = await _discovery.GetMetadataAsync(issuer, cancellationToken).ConfigureAwait(false);

        return (pdsUrl, issuer, metadata);
    }

    private async Task<string> PushAuthorizationRequestAsync(
        string parEndpoint,
        Dictionary<string, string> authParams,
        DPoPKeyPair dpopKey,
        CancellationToken cancellationToken)
    {
        var nonceCache = new DPoPNonceCache();

        // Try with cached nonce first
        var nonce = nonceCache.Get(parEndpoint);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            var proof = dpopKey.CreateProof("POST", parEndpoint, nonce);

            using var request = new HttpRequestMessage(HttpMethod.Post, parEndpoint);
            request.Headers.Add("DPoP", proof);

            var formContent = BuildFormContent(authParams);
            request.Content = new StringContent(formContent, Encoding.UTF8, "application/x-www-form-urlencoded");

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // Update nonce from response
            if (response.Headers.TryGetValues("DPoP-Nonce", out var nonceValues))
            {
                foreach (var n in nonceValues)
                {
                    nonceCache.Set(parEndpoint, n);
                    nonce = n;
                    break;
                }
            }

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var parResponse = JsonSerializer.Deserialize(content, OAuthJsonContext.Default.PushedAuthorizationResponse);

                if (parResponse == null || string.IsNullOrEmpty(parResponse.RequestUri))
                {
                    throw new OAuthException("invalid_par_response", "Invalid PAR response.");
                }

                return parResponse.RequestUri;
            }

            // Check for use_dpop_nonce error
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            try
            {
                var errorResponse = JsonSerializer.Deserialize(errorContent, OAuthJsonContext.Default.OAuthErrorResponse);
                if (errorResponse?.Error == "use_dpop_nonce" && attempt == 0)
                {
                    continue; // Retry with new nonce
                }

                throw new OAuthException(
                    errorResponse?.Error ?? "par_failed",
                    errorResponse?.ErrorDescription ?? errorContent);
            }
            catch (JsonException)
            {
                throw new OAuthException("par_failed", errorContent);
            }
        }

        throw new OAuthException("par_failed", "PAR request failed after retries.");
    }

    private async Task<TokenSet> ExchangeCodeAsync(
        string tokenEndpoint,
        string code,
        string verifier,
        DPoPKeyPair dpopKey,
        CancellationToken cancellationToken)
    {
        var nonceCache = new DPoPNonceCache();
        var nonce = nonceCache.Get(tokenEndpoint);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            var proof = dpopKey.CreateProof("POST", tokenEndpoint, nonce);

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
            request.Headers.Add("DPoP", proof);

            var formParams = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = _config.RedirectUri,
                ["client_id"] = _config.ClientId
            };

            var formContent = BuildFormContent(formParams);
            request.Content = new StringContent(formContent, Encoding.UTF8, "application/x-www-form-urlencoded");

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // Update nonce from response
            if (response.Headers.TryGetValues("DPoP-Nonce", out var nonceValues))
            {
                foreach (var n in nonceValues)
                {
                    nonceCache.Set(tokenEndpoint, n);
                    nonce = n;
                    break;
                }
            }

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var tokenResponse = JsonSerializer.Deserialize(content, OAuthJsonContext.Default.OAuthTokenResponse);

                if (tokenResponse == null)
                {
                    throw new OAuthException("invalid_token_response", "Invalid token response.");
                }

                return TokenSet.FromResponse(tokenResponse, string.Empty, string.Empty);
            }

            // Check for use_dpop_nonce error
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            try
            {
                var errorResponse = JsonSerializer.Deserialize(errorContent, OAuthJsonContext.Default.OAuthErrorResponse);
                if (errorResponse?.Error == "use_dpop_nonce" && attempt == 0)
                {
                    continue; // Retry with new nonce
                }

                throw new OAuthException(
                    errorResponse?.Error ?? "token_exchange_failed",
                    errorResponse?.ErrorDescription ?? errorContent);
            }
            catch (JsonException)
            {
                throw new OAuthException("token_exchange_failed", errorContent);
            }
        }

        throw new OAuthException("token_exchange_failed", "Token exchange failed after retries.");
    }

    private static string BuildAuthorizationUrl(string endpoint, Dictionary<string, string> parameters)
    {
        var sb = new StringBuilder(endpoint);
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

        return sb.ToString();
    }

    private static string BuildFormContent(Dictionary<string, string> parameters)
    {
        var sb = new StringBuilder();
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

        return sb.ToString();
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

        _discovery.Dispose();
        _identityResolver?.Dispose();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
