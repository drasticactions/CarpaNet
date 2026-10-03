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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly IdentityResolver _identityResolver;
    private readonly bool _ownsIdentityResolver;
    private readonly ILogger<OAuthSession> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private bool _disposed;

    /// <summary>
    /// Creates a new ATProto OAuth client.
    /// </summary>
    /// <param name="config">The OAuth client configuration.</param>
    public OAuthSession(OAuthClientConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        _loggerFactory = config.LoggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<OAuthSession>();

        _httpClient = config.HttpClient ?? new HttpClient();
        _ownsHttpClient = config.HttpClient == null;

        _stateStore = config.StateStore ?? new MemoryOAuthStateStore();
        _sessionStore = config.SessionStore ?? new MemoryOAuthSessionStore();
        _discovery = new AuthorizationServerDiscovery(_httpClient, loggerFactory: _loggerFactory);
        _ownsIdentityResolver = config.IdentityResolver == null;
        _identityResolver = config.IdentityResolver ?? new IdentityResolver(_httpClient, cache: new MemoryIdentityCache(), loggerFactory: _loggerFactory);
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
        _logger.LogInformation("Starting OAuth authorization for {Input}", input);

        // Resolve identity to find PDS and authorization server
        var (pdsUrl, issuer, serverMetadata, expectedSub) = await ResolveIdentityAsync(input, cancellationToken).ConfigureAwait(false);

        // Generate PKCE
        var (verifier, challenge) = Pkce.Generate();

        // Generate state
        var state = Pkce.GenerateState();

        // Generate DPoP key
        var dpopKey = await DPoPKeyPair.GenerateAsync().ConfigureAwait(false);

        // Store state data
        var stateData = new OAuthStateData
        {
            Issuer = issuer,
            DPoPKey = dpopKey.ExportKeyPair(),
            Verifier = verifier,
            AppState = appState,
            PdsUrl = pdsUrl,
            ExpectedSub = expectedSub,
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
            _logger.LogDebug("Attempting PAR to {Endpoint}", serverMetadata.PushedAuthorizationRequestEndpoint);
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
            catch (OAuthException ex)
            {
                _logger.LogWarning("PAR failed ({Error}): {Description}. Falling back to standard URL.", ex.ErrorCode, ex.Message);

                // If PAR is required by the server, don't silently fall back
                if (serverMetadata.RequirePushedAuthorizationRequests == true)
                {
                    throw;
                }

                // Fall back to standard authorization URL if PAR fails
            }
        }

        // Build standard authorization URL
        return BuildAuthorizationUrl(serverMetadata.AuthorizationEndpoint, authParams);
    }

    /// <summary>
    /// Handles the OAuth callback and exchanges the code for tokens.
    /// </summary>
    /// <remarks>
    /// The callback is validated before the session is created:
    /// <list type="bullet">
    /// <item><description>The <c>iss</c> parameter (RFC 9207) must match the issuer the flow was started with. It is
    /// required when the server advertises <c>authorization_response_iss_parameter_supported</c>.</description></item>
    /// <item><description>The token response's <c>sub</c> must be an atproto DID, must match the account the flow was
    /// started for (when started from a handle or DID), and the PDS in its DID document must be protected by the
    /// issuer that issued the tokens. The session's PDS URL is taken from that DID document.</description></item>
    /// </list>
    /// When <c>sub</c> validation fails, the tokens are revoked and the session is not stored.
    /// </remarks>
    /// <param name="callbackUrl">The full callback URL with query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The OAuth session.</returns>
    /// <exception cref="OAuthCallbackException">The callback contains an error, or fails <c>iss</c> or <c>sub</c> validation.</exception>
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
            _logger.LogWarning("OAuth callback error: {Error}", error);
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

        // Get code, state and issuer
        var code = query["code"];
        var stateParam = query["state"];
        var issParam = query["iss"];

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

            // Validate the iss parameter (RFC 9207) before redeeming the code
            ValidateIssuerParameter(issParam, storedState, serverMetadata);

            _logger.LogDebug("Exchanging authorization code");
            // Exchange code for tokens
            var tokenSet = await ExchangeCodeAsync(
                serverMetadata.TokenEndpoint,
                code,
                storedState.Verifier,
                dpopKey,
                cancellationToken).ConfigureAwait(false);

            tokenSet.Issuer = storedState.Issuer;

            // The token response MUST be verified before its "sub" can be trusted. The session's
            // PDS (DPoP audience) is the one from the sub's DID document, not the URL the flow
            // was started from (which may be an entryway).
            try
            {
                tokenSet.Audience = await VerifySubjectAsync(
                    tokenSet.Sub,
                    storedState,
                    serverMetadata,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await TryRevokeTokenAsync(serverMetadata, tokenSet, dpopKey).ConfigureAwait(false);
                throw;
            }

            // Create token provider
            var tokenProvider = new DPoPTokenProvider(
                _httpClient,
                _sessionStore,
                _discovery,
                _config.RefreshBuffer,
                _config.ClientId,
                _config.RedirectUri,
                _config.Scope,
                loggerFactory: _loggerFactory,
                identityResolver: _identityResolver);

            await tokenProvider.SetupAsync(
                tokenSet.Sub,
                tokenSet,
                dpopKey,
                serverMetadata,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("OAuth callback completed for {Did}", tokenSet.Sub);

            // Create session
            return new ATProtoOAuthClient(
                tokenSet.Sub,
                tokenSet.Audience,
                tokenProvider,
                this,
                storedState.AppState,
                identityResolver: _identityResolver,
                _config.JsonOptions,
                _config.LabelerDids,
                loggerFactory: _loggerFactory,
                httpClient: _httpClient);
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
        _logger.LogInformation("Restoring OAuth session for {Did}", sub);

        var tokenProvider = new DPoPTokenProvider(
            _httpClient,
            _sessionStore,
            _discovery,
            _config.RefreshBuffer,
            _config.ClientId,
            _config.RedirectUri,
            _config.Scope,
            loggerFactory: _loggerFactory,
            identityResolver: _identityResolver);

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
            this,
            null,
            identityResolver: _identityResolver,
            _config.JsonOptions,
            _config.LabelerDids,
            loggerFactory: _loggerFactory,
            httpClient: _httpClient);
    }

    /// <summary>
    /// Revokes a session's tokens.
    /// </summary>
    /// <param name="sub">The user's DID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RevokeAsync(string sub, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _logger.LogInformation("Revoking OAuth token for {Did}", sub);

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

            if (!string.IsNullOrEmpty(sessionData.TokenSet.RefreshToken))
            {
                var dpopKey = DPoPKeyPair.Import(sessionData.DPoPKey);
                try
                {
                    await TryRevokeTokenAsync(serverMetadata, sessionData.TokenSet, dpopKey).ConfigureAwait(false);
                }
                finally
                {
                    dpopKey.Dispose();
                }
            }
        }
        catch
        {
            _logger.LogWarning("Token revocation failed");
            // Ignore revocation errors
        }

        // Delete the session
        await _sessionStore.DeleteAsync(sub, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string pdsUrl, string issuer, OAuthAuthorizationServerMetadata metadata, string? did)> ResolveIdentityAsync(
        string input,
        CancellationToken cancellationToken)
    {
        string pdsUrl;
        string issuer;
        string? did = null;

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

            pdsUrl = didDoc.PdsEndpoint?.TrimEnd('/')
                ?? throw new OAuthException("pds_not_found", $"No PDS URL found for: {input}");

            // The account the user must sign in as
            did = !string.IsNullOrEmpty(didDoc.Id) ? didDoc.Id : null;
        }
        else
        {
            throw new OAuthException("identity_resolver_required", "Identity resolver required for handle/DID input.");
        }

        // Discover authorization server from PDS
        issuer = await _discovery.DiscoverAuthorizationServerAsync(pdsUrl, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Identity resolved: PDS={PdsUrl}, Issuer={Issuer}", pdsUrl, issuer);

        // Get server metadata
        var metadata = await _discovery.GetMetadataAsync(issuer, cancellationToken).ConfigureAwait(false);

        return (pdsUrl, issuer, metadata, did);
    }

    /// <summary>
    /// Validates the RFC 9207 <c>iss</c> authorization response parameter.
    /// </summary>
    private void ValidateIssuerParameter(
        string? issParam,
        OAuthStateData storedState,
        OAuthAuthorizationServerMetadata serverMetadata)
    {
        if (issParam != null)
        {
            if (!IsIssuer(issParam, storedState, serverMetadata))
            {
                _logger.LogWarning("Callback issuer mismatch: expected {Expected}, got {Actual}", storedState.Issuer, issParam);
                throw new OAuthCallbackException(
                    "issuer_mismatch",
                    $"Callback issuer '{issParam}' does not match expected issuer '{storedState.Issuer}'.",
                    storedState.AppState);
            }
        }
        else if (serverMetadata.AuthorizationResponseIssParameterSupported)
        {
            _logger.LogWarning("Callback is missing the iss parameter required by {Issuer}", storedState.Issuer);
            throw new OAuthCallbackException(
                "missing_iss",
                "The iss parameter is missing from the authorization response.",
                storedState.AppState);
        }
    }

    /// <summary>
    /// Verifies that the token response's subject is an atproto DID whose PDS is protected by the
    /// issuer that issued the tokens.
    /// </summary>
    /// <returns>The user's PDS URL (the resource server and DPoP audience).</returns>
    private async Task<string> VerifySubjectAsync(
        string sub,
        OAuthStateData storedState,
        OAuthAuthorizationServerMetadata serverMetadata,
        CancellationToken cancellationToken)
    {
        if (!AtprotoSyntax.IsAtprotoDid(sub))
        {
            throw new OAuthCallbackException(
                "invalid_sub",
                $"Token response subject '{sub}' is not a valid atproto DID.",
                storedState.AppState);
        }

        if (storedState.ExpectedSub != null && !string.Equals(sub, storedState.ExpectedSub, StringComparison.Ordinal))
        {
            _logger.LogWarning("Token subject {Sub} does not match expected {Expected}", sub, storedState.ExpectedSub);
            throw new OAuthCallbackException(
                "sub_mismatch",
                $"Token response subject '{sub}' does not match the account authorization was started for ('{storedState.ExpectedSub}').",
                storedState.AppState);
        }

        string pdsUrl;
        OAuthProtectedResourceMetadata resourceMetadata;
        try
        {
            // Always resolve fresh: a stale DID document could point to a previous PDS
            var didDoc = await _identityResolver.ResolveDidAsync(sub, skipCache: true, cancellationToken).ConfigureAwait(false);

            if (!string.Equals(didDoc.Id, sub, StringComparison.Ordinal))
            {
                throw new OAuthCallbackException(
                    "invalid_sub",
                    $"DID document id '{didDoc.Id}' does not match token subject '{sub}'.",
                    storedState.AppState);
            }

            var endpoint = didDoc.PdsEndpoint;
            if (string.IsNullOrEmpty(endpoint) ||
                !Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
                (endpointUri.Scheme != "https" && endpointUri.Scheme != "http"))
            {
                throw new OAuthCallbackException(
                    "pds_not_found",
                    $"No valid PDS endpoint found in the DID document of '{sub}'.",
                    storedState.AppState);
            }

            pdsUrl = endpoint!.TrimEnd('/');
            resourceMetadata = await _discovery.GetProtectedResourceMetadataAsync(pdsUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (OAuthCallbackException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OAuthCallbackException(
                "sub_verification_failed",
                $"Failed to verify token subject '{sub}': {ex.Message}",
                storedState.AppState,
                ex);
        }

        foreach (var server in resourceMetadata.AuthorizationServers!)
        {
            if (server != null && IsIssuer(server, storedState, serverMetadata))
            {
                _logger.LogDebug("Token subject {Sub} verified: PDS={PdsUrl}", sub, pdsUrl);
                return pdsUrl;
            }
        }

        // Best case: the user switched PDS. Worst case: a malicious server is trying to
        // impersonate a user. Either way, these tokens must not be used.
        _logger.LogWarning("PDS {PdsUrl} of {Sub} is not protected by issuer {Issuer}", pdsUrl, sub, storedState.Issuer);
        throw new OAuthCallbackException(
            "sub_issuer_mismatch",
            $"The PDS of '{sub}' ({pdsUrl}) is not protected by issuer '{storedState.Issuer}'.",
            storedState.AppState);
    }

    private static bool IsIssuer(string value, OAuthStateData storedState, OAuthAuthorizationServerMetadata serverMetadata)
    {
        // The stored issuer and the metadata issuer were verified to be the same issuer
        // (case-insensitively) when the metadata was fetched; accept either exact spelling.
        return string.Equals(value, serverMetadata.Issuer, StringComparison.Ordinal) ||
               string.Equals(value, storedState.Issuer, StringComparison.Ordinal);
    }

    /// <summary>
    /// Best-effort revocation of a token set at the authorization server. Revoking the refresh
    /// token revokes the whole grant; the access token is used when no refresh token exists.
    /// </summary>
    private async Task TryRevokeTokenAsync(
        OAuthAuthorizationServerMetadata serverMetadata,
        TokenSet tokenSet,
        DPoPKeyPair dpopKey)
    {
        var endpoint = serverMetadata.RevocationEndpoint;
        if (string.IsNullOrEmpty(endpoint))
        {
            return;
        }

        var (token, hint) = !string.IsNullOrEmpty(tokenSet.RefreshToken)
            ? (tokenSet.RefreshToken!, "refresh_token")
            : (tokenSet.AccessToken, "access_token");

        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            var proof = await dpopKey.CreateProofAsync("POST", endpoint!, null).ConfigureAwait(false);
            request.Headers.Add("DPoP", proof);

            var content = BuildFormContent(new Dictionary<string, string>
            {
                ["token"] = token,
                ["token_type_hint"] = hint,
                ["client_id"] = _config.ClientId
            });
            request.Content = new StringContent(content, Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Token revocation failed: {Message}", ex.Message);
        }
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
            var proof = await dpopKey.CreateProofAsync("POST", parEndpoint, nonce).ConfigureAwait(false);

            _logger.LogDebug("PAR attempt {Attempt}: endpoint={Endpoint}, nonce={Nonce}", attempt, parEndpoint, nonce ?? "(none)");

            // Log the decoded DPoP proof JWT for diagnostics
            var proofParts = proof.Split('.');
            if (proofParts.Length == 3)
            {
                var proofHeader = Encoding.UTF8.GetString(Pkce.Base64UrlDecode(proofParts[0]));
                var proofPayload = Encoding.UTF8.GetString(Pkce.Base64UrlDecode(proofParts[1]));
                var sigBytes = Pkce.Base64UrlDecode(proofParts[2]);
                _logger.LogDebug("DPoP proof header: {Header}", proofHeader);
                _logger.LogDebug("DPoP proof payload: {Payload}", proofPayload);
                _logger.LogDebug("DPoP proof signature: {SigLength} bytes", sigBytes.Length);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, parEndpoint);
            request.Headers.Add("DPoP", proof);

            var formContent = BuildFormContent(authParams);
            request.Content = new StringContent(formContent, Encoding.UTF8, "application/x-www-form-urlencoded");

            _logger.LogDebug("PAR request body: {Body}", formContent);

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("PAR response: {StatusCode}", (int)response.StatusCode);

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
            _logger.LogWarning("PAR failed with {StatusCode}: {ErrorBody}", (int)response.StatusCode, errorContent);

            try
            {
                var errorResponse = JsonSerializer.Deserialize(errorContent, OAuthJsonContext.Default.OAuthErrorResponse);
                if (errorResponse?.Error == "use_dpop_nonce" && attempt == 0)
                {
                    _logger.LogDebug("PAR retry: use_dpop_nonce, retrying with new nonce");
                    continue; // Retry with new nonce
                }

                var errorDesc = errorResponse?.ErrorDescription ?? errorContent;
                throw new OAuthException(
                    errorResponse?.Error ?? "par_failed",
                    $"PAR request to {parEndpoint} failed with HTTP {(int)response.StatusCode}: {errorDesc}");
            }
            catch (JsonException)
            {
                throw new OAuthException("par_failed", $"PAR request to {parEndpoint} failed with HTTP {(int)response.StatusCode}: {errorContent}");
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
            var proof = await dpopKey.CreateProofAsync("POST", tokenEndpoint, nonce).ConfigureAwait(false);

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

        if (_ownsIdentityResolver)
        {
            _identityResolver.Dispose();
        }

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
