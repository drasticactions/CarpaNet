using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarpaNet.Http;

/// <summary>
/// HTTP message handler that automatically handles rate limiting (429 responses)
/// with exponential backoff and jitter.
/// </summary>
public sealed class RateLimitHandler : DelegatingHandler
{
    private static readonly Random Random = new Random();
    private readonly ILogger<RateLimitHandler> _logger;

    /// <summary>
    /// Gets or sets whether to automatically retry on rate limit responses.
    /// Default is true.
    /// </summary>
    public bool AutoRetryOnRateLimit { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts.
    /// Default is 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay for exponential backoff.
    /// Default is 1 second.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum delay between retries.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the jitter factor (0.0 to 1.0).
    /// Adds randomness to delays to prevent thundering herd.
    /// Default is 0.2 (20% jitter).
    /// </summary>
    public double JitterFactor { get; set; } = 0.2;

    /// <summary>
    /// Event raised when a rate limit is encountered.
    /// </summary>
    public event EventHandler<RateLimitEventArgs>? RateLimitEncountered;

    /// <summary>
    /// Creates a new RateLimitHandler with default settings.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory for diagnostic logging.</param>
    public RateLimitHandler(ILoggerFactory? loggerFactory = null)
    {
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<RateLimitHandler>();
    }

    /// <summary>
    /// Creates a new RateLimitHandler with a specified inner handler.
    /// </summary>
    /// <param name="innerHandler">The inner handler.</param>
    /// <param name="loggerFactory">Optional logger factory for diagnostic logging.</param>
    public RateLimitHandler(HttpMessageHandler innerHandler, ILoggerFactory? loggerFactory = null)
        : base(innerHandler)
    {
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<RateLimitHandler>();
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        HttpResponseMessage? response = null;

        while (true)
        {
            attempt++;

            // Clone request for retry (requests can only be sent once)
            using var requestClone = attempt > 1 ? await CloneRequestAsync(request).ConfigureAwait(false) : null;
            var requestToSend = requestClone ?? request;

            response = await base.SendAsync(requestToSend, cancellationToken).ConfigureAwait(false);

            // Check for rate limit
            if (response.StatusCode != (HttpStatusCode)429)
            {
                return response;
            }

            // Parse rate limit info
            var rateLimitInfo = RateLimitInfo.FromResponse(response);

            // Raise event
            RateLimitEncountered?.Invoke(this, new RateLimitEventArgs(rateLimitInfo, attempt));

            // Check if we should retry
            if (!AutoRetryOnRateLimit || attempt >= MaxRetries)
            {
                _logger.LogWarning("Rate limit max retries exceeded");
                return response;
            }

            _logger.LogWarning("Rate limited (429) on attempt {Attempt}/{MaxRetries}", attempt, MaxRetries);

            // Calculate delay
            var delay = CalculateDelay(rateLimitInfo, attempt);
            _logger.LogDebug("Rate limit retry after {DelayMs}ms", (int)delay.TotalMilliseconds);

            // Dispose the response before retrying
            response.Dispose();

            // Wait before retrying
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan CalculateDelay(RateLimitInfo? rateLimitInfo, int attempt)
    {
        TimeSpan baseDelay;

        // Prefer server-specified retry-after
        var retryAfter = rateLimitInfo?.GetRetryAfter();
        if (retryAfter.HasValue)
        {
            baseDelay = retryAfter.Value;
        }
        else
        {
            // Exponential backoff: baseDelay * 2^(attempt-1)
            var exponentialDelay = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
            baseDelay = TimeSpan.FromMilliseconds(Math.Min(exponentialDelay, MaxDelay.TotalMilliseconds));
        }

        // Add jitter
        if (JitterFactor > 0)
        {
            var jitterRange = baseDelay.TotalMilliseconds * JitterFactor;
            var jitter = (Random.NextDouble() * 2 - 1) * jitterRange; // -jitter to +jitter
            baseDelay = TimeSpan.FromMilliseconds(Math.Max(0, baseDelay.TotalMilliseconds + jitter));
        }

        // Clamp to max delay
        if (baseDelay > MaxDelay)
        {
            baseDelay = MaxDelay;
        }

        return baseDelay;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        // Copy headers
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Copy content
        if (request.Content != null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(contentBytes);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

#if NET5_0_OR_GREATER
        // Copy options
        foreach (var option in request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }
#endif

        return clone;
    }
}

/// <summary>
/// Event arguments for rate limit events.
/// </summary>
public sealed class RateLimitEventArgs : EventArgs
{
    /// <summary>
    /// Gets the rate limit information from the response.
    /// </summary>
    public RateLimitInfo? RateLimitInfo { get; }

    /// <summary>
    /// Gets the current attempt number.
    /// </summary>
    public int Attempt { get; }

    /// <summary>
    /// Creates new rate limit event arguments.
    /// </summary>
    public RateLimitEventArgs(RateLimitInfo? rateLimitInfo, int attempt)
    {
        RateLimitInfo = rateLimitInfo;
        Attempt = attempt;
    }
}
