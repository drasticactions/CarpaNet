using System;

namespace CarpaNet.OAuth;

/// <summary>
/// Base exception for OAuth errors.
/// </summary>
public class OAuthException : Exception
{
    /// <summary>
    /// The OAuth error code.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// The error description.
    /// </summary>
    public string? ErrorDescription { get; }

    /// <summary>
    /// Creates a new OAuth exception.
    /// </summary>
    public OAuthException(string errorCode, string? errorDescription = null)
        : base(FormatMessage(errorCode, errorDescription))
    {
        ErrorCode = errorCode;
        ErrorDescription = errorDescription;
    }

    /// <summary>
    /// Creates a new OAuth exception with an inner exception.
    /// </summary>
    public OAuthException(string errorCode, string? errorDescription, Exception innerException)
        : base(FormatMessage(errorCode, errorDescription), innerException)
    {
        ErrorCode = errorCode;
        ErrorDescription = errorDescription;
    }

    private static string FormatMessage(string errorCode, string? errorDescription)
    {
        return string.IsNullOrEmpty(errorDescription)
            ? errorCode
            : $"{errorCode}: {errorDescription}";
    }
}

/// <summary>
/// Exception thrown when an OAuth callback contains an error.
/// </summary>
public class OAuthCallbackException : OAuthException
{
    /// <summary>
    /// The application state that was passed to the authorize call.
    /// </summary>
    public string? AppState { get; }

    /// <summary>
    /// Creates a new OAuth callback exception.
    /// </summary>
    public OAuthCallbackException(string errorCode, string? errorDescription, string? appState)
        : base(errorCode, errorDescription)
    {
        AppState = appState;
    }
}

/// <summary>
/// Exception thrown when token refresh fails.
/// </summary>
public class TokenRefreshException : OAuthException
{
    /// <summary>
    /// The subject (DID) of the session that failed to refresh.
    /// </summary>
    public string? Sub { get; }

    /// <summary>
    /// Creates a new token refresh exception.
    /// </summary>
    public TokenRefreshException(string errorCode, string? errorDescription, string? sub)
        : base(errorCode, errorDescription)
    {
        Sub = sub;
    }

    /// <summary>
    /// Creates a new token refresh exception with an inner exception.
    /// </summary>
    public TokenRefreshException(string errorCode, string? errorDescription, string? sub, Exception innerException)
        : base(errorCode, errorDescription, innerException)
    {
        Sub = sub;
    }
}

/// <summary>
/// Exception thrown when a DPoP nonce is required but not provided or invalid.
/// </summary>
public class DPoPNonceException : OAuthException
{
    /// <summary>
    /// The new nonce provided by the server.
    /// </summary>
    public string? NewNonce { get; }

    /// <summary>
    /// Creates a new DPoP nonce exception.
    /// </summary>
    public DPoPNonceException(string? newNonce)
        : base("use_dpop_nonce", "Server requires a DPoP nonce")
    {
        NewNonce = newNonce;
    }
}
