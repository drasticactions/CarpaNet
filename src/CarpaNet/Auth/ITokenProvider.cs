using System;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.Auth;

/// <summary>
/// Provides access tokens for authenticated ATProtocol requests.
/// Implementations handle token acquisition and refresh for different authentication methods.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Gets whether the provider currently has a valid (non-expired) token.
    /// </summary>
    bool HasValidToken { get; }

    /// <summary>
    /// Gets the DID of the authenticated user, or null if not authenticated.
    /// </summary>
    string? CurrentDid { get; }

    /// <summary>
    /// Gets the PDS URL for the authenticated user, or null if not authenticated.
    /// </summary>
    Uri? PdsUrl { get; }

    /// <summary>
    /// Gets an access token for making authenticated requests.
    /// May trigger a refresh if the current token is expired or about to expire.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The access token, or null if not authenticated.</returns>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a token refresh regardless of expiry status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when tokens are refreshed.
    /// Consumers can use this to persist updated tokens.
    /// </summary>
    event EventHandler<TokenRefreshedEventArgs>? TokenRefreshed;
}

/// <summary>
/// Event arguments for the TokenRefreshed event.
/// </summary>
public class TokenRefreshedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the new access token (JWT).
    /// </summary>
    public string AccessToken { get; }

    /// <summary>
    /// Gets the new refresh token (JWT).
    /// </summary>
    public string RefreshToken { get; }

    /// <summary>
    /// Gets the DID of the authenticated user.
    /// </summary>
    public string Did { get; }

    /// <summary>
    /// Gets the handle of the authenticated user.
    /// </summary>
    public string? Handle { get; }

    /// <summary>
    /// Creates a new TokenRefreshedEventArgs.
    /// </summary>
    public TokenRefreshedEventArgs(string accessToken, string refreshToken, string did, string? handle)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        Did = did;
        Handle = handle;
    }
}
