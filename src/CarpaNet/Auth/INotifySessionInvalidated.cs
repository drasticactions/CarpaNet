using System;

namespace CarpaNet.Auth;

/// <summary>
/// Implemented by token providers that can tell when a session has ended for good
/// (for example, a revoked or expired refresh token).
/// </summary>
/// <remarks>
/// <see cref="SessionTokenProvider"/> and the OAuth DPoP token provider implement this.
/// A temporary failure (network error, 5xx, rate limit) does not raise the event.
/// </remarks>
public interface INotifySessionInvalidated
{
    /// <summary>
    /// Raised once when a token refresh is rejected by the server and the session cannot continue.
    /// The provider drops its tokens before raising it; the caller decides whether to delete
    /// stored session data and ask the user to sign in again.
    /// </summary>
    event EventHandler<SessionInvalidatedEventArgs>? SessionInvalidated;
}

/// <summary>
/// Event arguments for <see cref="INotifySessionInvalidated.SessionInvalidated"/>.
/// </summary>
public sealed class SessionInvalidatedEventArgs : EventArgs
{
    /// <summary>
    /// Creates new event arguments.
    /// </summary>
    /// <param name="did">The DID of the session that ended, if known.</param>
    /// <param name="reason">The server's error code (such as <c>ExpiredToken</c> or <c>invalid_grant</c>), or a status description.</param>
    /// <param name="exception">The exception from the failed refresh, if any.</param>
    public SessionInvalidatedEventArgs(string? did, string reason, Exception? exception)
    {
        Did = did;
        Reason = reason;
        Exception = exception;
    }

    /// <summary>
    /// Gets the DID of the session that ended, if known.
    /// </summary>
    public string? Did { get; }

    /// <summary>
    /// Gets the server's error code, or a status description when there was none.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the exception from the failed refresh, if any.
    /// </summary>
    public Exception? Exception { get; }
}
