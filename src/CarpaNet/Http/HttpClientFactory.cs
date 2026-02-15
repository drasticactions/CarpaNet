using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace CarpaNet.Http;

/// <summary>
/// Factory for creating optimized HttpClient instances for ATProtocol.
/// </summary>
public static class HttpClientFactory
{
    /// <summary>
    /// Default connection pool idle timeout.
    /// </summary>
    public static readonly TimeSpan DefaultPooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Default connection lifetime for connection recycling.
    /// </summary>
    public static readonly TimeSpan DefaultPooledConnectionLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Creates an optimized HttpClient for ATProtocol operations.
    /// </summary>
    /// <param name="options">Optional configuration options.</param>
    /// <returns>A configured HttpClient.</returns>
    public static HttpClient Create(HttpClientFactoryOptions? options = null)
    {
        options ??= new HttpClientFactoryOptions();

        var handler = CreateHandler(options);
        var client = new HttpClient(handler, disposeHandler: true);

        if (options.Timeout.HasValue)
        {
            client.Timeout = options.Timeout.Value;
        }

        // Set default headers
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        if (!string.IsNullOrEmpty(options.UserAgent))
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        }

        return client;
    }

    /// <summary>
    /// Creates an optimized HttpMessageHandler for ATProtocol operations.
    /// </summary>
    /// <param name="options">Optional configuration options.</param>
    /// <returns>A configured handler chain.</returns>
    public static HttpMessageHandler CreateHandler(HttpClientFactoryOptions? options = null)
    {
        options ??= new HttpClientFactoryOptions();

        HttpMessageHandler handler;

#if NET5_0_OR_GREATER
        // Use SocketsHttpHandler for best performance
        var socketsHandler = new SocketsHttpHandler
        {
            // Connection pooling
            PooledConnectionIdleTimeout = options.PooledConnectionIdleTimeout ?? DefaultPooledConnectionIdleTimeout,
            PooledConnectionLifetime = options.PooledConnectionLifetime ?? DefaultPooledConnectionLifetime,
            MaxConnectionsPerServer = options.MaxConnectionsPerServer ?? 10,

            // Enable automatic decompression
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,

            // Connection settings
            ConnectTimeout = options.ConnectTimeout ?? TimeSpan.FromSeconds(30),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),

            // Enable cookies if needed
            UseCookies = options.UseCookies,
        };

        // Enable HTTP/2 if supported
        if (options.EnableHttp2)
        {
            socketsHandler.EnableMultipleHttp2Connections = true;
        }

        handler = socketsHandler;
#else
        // Fallback for older frameworks
        var httpHandler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = options.UseCookies,
            MaxConnectionsPerServer = options.MaxConnectionsPerServer ?? 10
        };

        handler = httpHandler;
#endif

        // Add rate limit handler if enabled
        if (options.EnableRateLimitHandler)
        {
            var rateLimitHandler = new RateLimitHandler(handler, loggerFactory: options.LoggerFactory)
            {
                AutoRetryOnRateLimit = options.AutoRetryOnRateLimit,
                MaxRetries = options.RateLimitMaxRetries
            };
            handler = rateLimitHandler;
        }

        return handler;
    }
}

/// <summary>
/// Options for HttpClient factory.
/// </summary>
public sealed class HttpClientFactoryOptions
{
    /// <summary>
    /// Gets or sets the request timeout.
    /// Default is 100 seconds (HttpClient default).
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the User-Agent header value.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Gets or sets the pooled connection idle timeout.
    /// Default is 2 minutes.
    /// </summary>
    public TimeSpan? PooledConnectionIdleTimeout { get; set; }

    /// <summary>
    /// Gets or sets the pooled connection lifetime.
    /// Connections are recycled after this time to handle DNS changes.
    /// Default is 10 minutes.
    /// </summary>
    public TimeSpan? PooledConnectionLifetime { get; set; }

    /// <summary>
    /// Gets or sets the maximum connections per server.
    /// Default is 10.
    /// </summary>
    public int? MaxConnectionsPerServer { get; set; }

    /// <summary>
    /// Gets or sets the connection timeout.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan? ConnectTimeout { get; set; }

    /// <summary>
    /// Gets or sets whether to enable HTTP/2 multiplexing.
    /// Default is true.
    /// </summary>
    public bool EnableHttp2 { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to use cookies.
    /// Default is false.
    /// </summary>
    public bool UseCookies { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to enable the rate limit handler.
    /// Default is true.
    /// </summary>
    public bool EnableRateLimitHandler { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to automatically retry on rate limit.
    /// Only applies if EnableRateLimitHandler is true.
    /// Default is true.
    /// </summary>
    public bool AutoRetryOnRateLimit { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of rate limit retries.
    /// Only applies if EnableRateLimitHandler is true.
    /// Default is 3.
    /// </summary>
    public int RateLimitMaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the logger factory for diagnostic logging.
    /// When null, logging is disabled.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }
}
