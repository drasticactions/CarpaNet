using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Http;
using CarpaNet.Identity;
using CarpaNet.Storage;
using CarpaNet.Xrpc;

namespace CarpaNet.Auth;

/// <summary>
/// Token provider that uses ATProtocol session tokens (createSession/refreshSession).
/// Suitable for App Passwords and direct username/password authentication.
/// </summary>
public sealed class SessionTokenProvider : ITokenProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
    private readonly TimeSpan _refreshBuffer;
    private readonly ISessionStore? _sessionStore;

    private string? _accessJwt;
    private string? _refreshJwt;
    private DateTimeOffset _accessExpiry;
    private string? _did;
    private string? _handle;
    private Uri? _pdsUrl;
    private bool _disposed;

    /// <inheritdoc/>
    public bool HasValidToken => !string.IsNullOrEmpty(_accessJwt) && DateTimeOffset.UtcNow < _accessExpiry;

    /// <inheritdoc/>
    public string? CurrentDid => _did;

    /// <inheritdoc/>
    public Uri? PdsUrl => _pdsUrl;

    /// <summary>
    /// Gets the current handle of the authenticated user.
    /// </summary>
    public string? Handle => _handle;

    /// <summary>
    /// Gets the current access JWT token.
    /// </summary>
    public string? AccessJwt => _accessJwt;

    /// <summary>
    /// Gets the current refresh JWT token.
    /// </summary>
    public string? RefreshJwt => _refreshJwt;

    /// <summary>
    /// Gets when the current access token expires.
    /// </summary>
    public DateTimeOffset AccessExpiry => _accessExpiry;

    /// <inheritdoc/>
    public event EventHandler<TokenRefreshedEventArgs>? TokenRefreshed;

    /// <summary>
    /// Creates a new SessionTokenProvider with default settings.
    /// Creates a new HttpClient that will be disposed with this provider.
    /// </summary>
    /// <param name="refreshBuffer">Time before expiry to trigger proactive refresh (default: 30 seconds).</param>
    /// <param name="sessionStore">Optional session store for automatic persistence.</param>
    public SessionTokenProvider(TimeSpan? refreshBuffer = null, ISessionStore? sessionStore = null)
        : this(new HttpClient(), ownsHttpClient: true, refreshBuffer, sessionStore)
    {
    }

    /// <summary>
    /// Creates a new SessionTokenProvider with a custom HttpClient.
    /// </summary>
    /// <param name="httpClient">The HttpClient to use for auth requests.</param>
    /// <param name="refreshBuffer">Time before expiry to trigger proactive refresh (default: 30 seconds).</param>
    /// <param name="sessionStore">Optional session store for automatic persistence.</param>
    public SessionTokenProvider(HttpClient httpClient, TimeSpan? refreshBuffer = null, ISessionStore? sessionStore = null)
        : this(httpClient, ownsHttpClient: false, refreshBuffer, sessionStore)
    {
    }

    private SessionTokenProvider(HttpClient httpClient, bool ownsHttpClient, TimeSpan? refreshBuffer, ISessionStore? sessionStore)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
        _refreshBuffer = refreshBuffer ?? TimeSpan.FromSeconds(30);
        _sessionStore = sessionStore;
    }

    /// <summary>
    /// Creates a session by logging in with credentials.
    /// </summary>
    /// <param name="identifier">The user identifier (handle, email, or DID).</param>
    /// <param name="password">The password or App Password.</param>
    /// <param name="serviceUrl">The service URL to authenticate against (default: Bluesky Entryway).</param>
    /// <param name="authFactorToken">Optional 2FA token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session response.</returns>
    public async Task<SessionResponse> LoginAsync(
        string identifier,
        string password,
        Uri? serviceUrl = null,
        string? authFactorToken = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        serviceUrl ??= new Uri(BlueskyServices.Entryway);

        var request = new CreateSessionRequest
        {
            Identifier = identifier,
            Password = password,
            AuthFactorToken = authFactorToken
        };

        var url = XrpcHttpHandler.BuildUrl(serviceUrl, "com.atproto.server.createSession");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, SessionJsonContext.Default.CreateSessionRequest),
                Encoding.UTF8,
                "application/json")
        };

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await XrpcHttpHandler.ThrowForErrorResponseAsync(response, cancellationToken).ConfigureAwait(false);
        }

#if NET8_0_OR_GREATER
        var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var session = await JsonSerializer.DeserializeAsync(content, SessionJsonContext.Default.SessionResponse, cancellationToken).ConfigureAwait(false);
#else
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var session = JsonSerializer.Deserialize(content, SessionJsonContext.Default.SessionResponse);
#endif

        if (session == null)
        {
            throw new ATProtoException("Failed to parse session response.");
        }

        // Store session data
        await UpdateSessionAsync(session, serviceUrl).ConfigureAwait(false);

        return session;
    }

    /// <summary>
    /// Restores a session from stored tokens.
    /// Does not validate the tokens - call RefreshAsync to ensure they're valid.
    /// </summary>
    /// <param name="accessJwt">The stored access JWT.</param>
    /// <param name="refreshJwt">The stored refresh JWT.</param>
    /// <param name="did">The user's DID.</param>
    /// <param name="handle">The user's handle.</param>
    /// <param name="pdsUrl">The PDS URL.</param>
    public void RestoreSession(string accessJwt, string refreshJwt, string did, string? handle, Uri pdsUrl)
    {
        ThrowIfDisposed();

        _accessJwt = accessJwt ?? throw new ArgumentNullException(nameof(accessJwt));
        _refreshJwt = refreshJwt ?? throw new ArgumentNullException(nameof(refreshJwt));
        _did = did ?? throw new ArgumentNullException(nameof(did));
        _handle = handle;
        _pdsUrl = pdsUrl ?? throw new ArgumentNullException(nameof(pdsUrl));
        _accessExpiry = ParseJwtExpiry(accessJwt);
    }

    /// <inheritdoc/>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(_accessJwt))
        {
            return null;
        }

        // Check if we need to refresh (with buffer before actual expiry)
        if (DateTimeOffset.UtcNow >= _accessExpiry - _refreshBuffer)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return _accessJwt;
    }

    /// <inheritdoc/>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(_refreshJwt) || _pdsUrl == null)
        {
            throw new InvalidOperationException("No refresh token available. Call LoginAsync first.");
        }

        // Use lock to prevent concurrent refresh attempts
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (HasValidToken)
            {
                return;
            }

            var url = XrpcHttpHandler.BuildUrl(_pdsUrl, "com.atproto.server.refreshSession");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _refreshJwt);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await XrpcHttpHandler.ThrowForErrorResponseAsync(response, cancellationToken).ConfigureAwait(false);
            }

#if NET8_0_OR_GREATER
            var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var session = await JsonSerializer.DeserializeAsync(content, SessionJsonContext.Default.SessionResponse, cancellationToken).ConfigureAwait(false);
#else
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var session = JsonSerializer.Deserialize(content, SessionJsonContext.Default.SessionResponse);
#endif

            if (session == null)
            {
                throw new ATProtoException("Failed to parse refresh session response.");
            }

            await UpdateSessionAsync(session, _pdsUrl).ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Clears the current session.
    /// </summary>
    public void ClearSession()
    {
        _accessJwt = null;
        _refreshJwt = null;
        _did = null;
        _handle = null;
        _pdsUrl = null;
        _accessExpiry = default;
    }

    /// <summary>
    /// Clears the current session and deletes it from the store if configured.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        var did = _did;
        ClearSession();

        if (_sessionStore != null && !string.IsNullOrEmpty(did))
        {
            await _sessionStore.DeleteAsync(did, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Restores a session from the session store.
    /// </summary>
    /// <param name="sub">The user's DID (subject).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the session was restored, false if no stored session was found.</returns>
    public async Task<bool> RestoreSessionAsync(string sub, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_sessionStore == null)
        {
            throw new InvalidOperationException("No session store configured.");
        }

        var data = await _sessionStore.GetAsync(sub, cancellationToken).ConfigureAwait(false);
        if (data == null)
        {
            return false;
        }

        RestoreSession(data.AccessJwt, data.RefreshJwt, data.Did, data.Handle, new Uri(data.PdsUrl));
        return true;
    }

    private async Task UpdateSessionAsync(SessionResponse session, Uri pdsUrl)
    {
        _accessJwt = session.AccessJwt;
        _refreshJwt = session.RefreshJwt;
        _did = session.Did;
        _handle = session.Handle;
        _pdsUrl = pdsUrl;
        _accessExpiry = ParseJwtExpiry(session.AccessJwt);

        // Persist to store if configured
        if (_sessionStore != null && !string.IsNullOrEmpty(session.Did))
        {
            await _sessionStore.StoreAsync(session.Did, new SessionData
            {
                AccessJwt = session.AccessJwt,
                RefreshJwt = session.RefreshJwt,
                Did = session.Did,
                Handle = session.Handle,
                PdsUrl = pdsUrl.ToString()
            }).ConfigureAwait(false);
        }

        // Notify listeners
        TokenRefreshed?.Invoke(this, new TokenRefreshedEventArgs(
            session.AccessJwt,
            session.RefreshJwt,
            session.Did,
            session.Handle));
    }

    /// <summary>
    /// Parses the expiry time from a JWT token.
    /// </summary>
    /// <param name="jwt">The JWT token.</param>
    /// <returns>The expiry time, or MinValue if parsing fails.</returns>
    public static DateTimeOffset ParseJwtExpiry(string jwt)
    {
        if (string.IsNullOrEmpty(jwt))
        {
            return DateTimeOffset.MinValue;
        }

        try
        {
            // JWT format: header.payload.signature
            var parts = jwt.Split('.');
            if (parts.Length != 3)
            {
                return DateTimeOffset.MinValue;
            }

            // Decode payload (base64url)
            var payload = parts[1];

            // Add padding if needed
            var remainder = payload.Length % 4;
            string padded;
            if (remainder == 2)
            {
                padded = payload + "==";
            }
            else if (remainder == 3)
            {
                padded = payload + "=";
            }
            else
            {
                padded = payload;
            }

            // Convert base64url to base64
            var base64 = padded.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(base64);
            var json = Encoding.UTF8.GetString(bytes);

            // Parse JSON to extract exp claim
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var expElement))
            {
                var exp = expElement.GetInt64();
                return DateTimeOffset.FromUnixTimeSeconds(exp);
            }
        }
        catch
        {
            // If parsing fails, return min value (will trigger refresh)
        }

        return DateTimeOffset.MinValue;
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
        _refreshLock.Dispose();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
