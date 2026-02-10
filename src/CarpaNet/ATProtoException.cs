using System;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace CarpaNet;

/// <summary>
/// Base exception for ATProtocol/XRPC errors.
/// </summary>
public class ATProtoException : Exception
{
    /// <summary>
    /// Gets the XRPC error code (e.g., "InvalidRequest", "ExpiredToken").
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the HTTP status code from the response.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Creates a new ATProtoException.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The XRPC error code.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    public ATProtoException(string message, string? errorCode = null, HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    /// <summary>
    /// Creates a new ATProtoException with an inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <param name="errorCode">The XRPC error code.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    public ATProtoException(string message, Exception innerException, string? errorCode = null, HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}

/// <summary>
/// Exception thrown when authentication fails (401 errors).
/// </summary>
public class AuthenticationException : ATProtoException
{
    /// <summary>
    /// Gets whether the token should be refreshed.
    /// This is true for "ExpiredToken" errors.
    /// </summary>
    public bool ShouldRefresh => string.Equals(ErrorCode, "ExpiredToken", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new AuthenticationException.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The XRPC error code.</param>
    public AuthenticationException(string message, string? errorCode = null)
        : base(message, errorCode, HttpStatusCode.Unauthorized)
    {
    }

    /// <summary>
    /// Creates a new AuthenticationException with an inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <param name="errorCode">The XRPC error code.</param>
    public AuthenticationException(string message, Exception innerException, string? errorCode = null)
        : base(message, innerException, errorCode, HttpStatusCode.Unauthorized)
    {
    }
}

/// <summary>
/// Exception thrown when rate limited (429 errors).
/// </summary>
public class RateLimitException : ATProtoException
{
    /// <summary>
    /// Gets the rate limit information from the response headers.
    /// </summary>
    public RateLimitInfo? RateLimitInfo { get; }

    /// <summary>
    /// Gets the time to wait before retrying, if available.
    /// </summary>
    public TimeSpan? RetryAfter => RateLimitInfo?.GetRetryAfter();

    /// <summary>
    /// Creates a new RateLimitException.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="rateLimitInfo">The rate limit information.</param>
    /// <param name="errorCode">The XRPC error code.</param>
    public RateLimitException(string message, RateLimitInfo? rateLimitInfo = null, string? errorCode = null)
        : base(message, errorCode, (HttpStatusCode)429)
    {
        RateLimitInfo = rateLimitInfo;
    }
}

/// <summary>
/// Exception thrown for validation errors (400 errors).
/// </summary>
public class ValidationException : ATProtoException
{
    /// <summary>
    /// Creates a new ValidationException.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The XRPC error code.</param>
    public ValidationException(string message, string? errorCode = null)
        : base(message, errorCode, HttpStatusCode.BadRequest)
    {
    }
}

/// <summary>
/// Rate limit information parsed from response headers.
/// </summary>
public sealed class RateLimitInfo
{
    /// <summary>
    /// Gets or sets the total number of requests allowed in the window (RateLimit-Limit).
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Gets or sets the number of requests remaining in the current window (RateLimit-Remaining).
    /// </summary>
    public int? Remaining { get; set; }

    /// <summary>
    /// Gets or sets the Unix timestamp when the rate limit resets (RateLimit-Reset).
    /// </summary>
    public long? ResetUnixTime { get; set; }

    /// <summary>
    /// Gets or sets the rate limit policy string (RateLimit-Policy).
    /// </summary>
    public string? Policy { get; set; }

    /// <summary>
    /// Gets or sets the Retry-After header value in seconds, if present.
    /// </summary>
    public int? RetryAfterSeconds { get; set; }

    /// <summary>
    /// Gets the reset time as a DateTimeOffset.
    /// </summary>
    public DateTimeOffset? ResetTime =>
        ResetUnixTime.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(ResetUnixTime.Value)
            : null;

    /// <summary>
    /// Calculates the time to wait before retrying.
    /// Prefers Retry-After header, falls back to RateLimit-Reset.
    /// </summary>
    /// <returns>The time to wait, or null if not determinable.</returns>
    public TimeSpan? GetRetryAfter()
    {
        if (RetryAfterSeconds.HasValue)
        {
            return TimeSpan.FromSeconds(RetryAfterSeconds.Value);
        }

        if (ResetTime.HasValue)
        {
            var wait = ResetTime.Value - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    /// <summary>
    /// Parses rate limit information from HTTP response headers.
    /// </summary>
    /// <param name="getHeader">Function to get header values by name.</param>
    /// <returns>The parsed rate limit info, or null if no rate limit headers present.</returns>
    public static RateLimitInfo? FromHeaders(Func<string, string?> getHeader)
    {
        var limit = ParseInt(getHeader("RateLimit-Limit"));
        var remaining = ParseInt(getHeader("RateLimit-Remaining"));
        var reset = ParseLong(getHeader("RateLimit-Reset"));
        var policy = getHeader("RateLimit-Policy");
        var retryAfter = ParseInt(getHeader("Retry-After"));

        if (limit == null && remaining == null && reset == null && policy == null && retryAfter == null)
        {
            return null;
        }

        return new RateLimitInfo
        {
            Limit = limit,
            Remaining = remaining,
            ResetUnixTime = reset,
            Policy = policy,
            RetryAfterSeconds = retryAfter
        };
    }

    /// <summary>
    /// Parses rate limit information from an HTTP response message.
    /// </summary>
    /// <param name="response">The HTTP response message.</param>
    /// <returns>The parsed rate limit info, or null if no rate limit headers present.</returns>
    public static RateLimitInfo? FromResponse(HttpResponseMessage response)
    {
        return FromHeaders(name =>
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                return values.FirstOrDefault();
            }
            return null;
        });
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        return int.TryParse(value, out var result) ? result : null;
    }

    private static long? ParseLong(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        return long.TryParse(value, out var result) ? result : null;
    }
}
