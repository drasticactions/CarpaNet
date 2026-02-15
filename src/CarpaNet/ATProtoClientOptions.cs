using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using CarpaNet.Auth;
using CarpaNet.Cbor;
using CarpaNet.Http;
using CarpaNet.Identity;
using CarpaNet.Storage;
using Microsoft.Extensions.Logging;

namespace CarpaNet;

/// <summary>
/// Configuration options for ATProtoClient.
/// </summary>
public sealed class ATProtoClientOptions
{
    /// <summary>
    /// Gets or sets the base URL for API requests.
    /// Defaults to the Bluesky public AppView for public clients,
    /// or should be set to the user's PDS for authenticated clients.
    /// </summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the HttpClient to use for requests.
    /// If not provided, a new HttpClient will be created.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Gets or sets the token provider for authentication.
    /// If null, the client operates in public (unauthenticated) mode.
    /// </summary>
    public ITokenProvider? TokenProvider { get; set; }

    /// <summary>
    /// Gets or sets the identity resolver for handle/DID resolution.
    /// If null, a new resolver will be created if needed.
    /// </summary>
    public IdentityResolver? IdentityResolver { get; set; }

    /// <summary>
    /// Gets or sets the JSON serializer options.
    /// Must be provided with a source-generated IJsonTypeInfoResolver for AOT compatibility.
    /// </summary>
    public JsonSerializerOptions JsonOptions { get; set; } = null!;

    /// <summary>
    /// Gets or sets the CBOR serializer context for event streams.
    /// Must be provided with a source-generated CborSerializerContext for AOT compatibility.
    /// </summary>
    public CborSerializerContext CborContext { get; set; } = null!;

    /// <summary>
    /// Gets or sets the list of labeler DIDs to accept labels from.
    /// These are included in the atproto-accept-labelers header.
    /// </summary>
    public IReadOnlyList<string>? LabelerDids { get; set; }

    /// <summary>
    /// Gets or sets the request timeout.
    /// Default is 100 seconds (HttpClient default).
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets whether to automatically retry on 401 errors by refreshing the token.
    /// Default is true.
    /// </summary>
    public bool AutoRetryOnAuthFailure { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to create an identity resolver if not provided.
    /// Default is true.
    /// </summary>
    public bool CreateIdentityResolver { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable rate limit handling.
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
    /// Gets or sets the User-Agent header value.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Gets or sets the session store for automatic persistence of password-based sessions.
    /// Used by <see cref="ATProtoClient.Create"/> and session lifecycle methods.
    /// Defaults to <see cref="MemorySessionStore"/> if not provided.
    /// </summary>
    public ISessionStore SessionStore { get; set; } = new MemorySessionStore();

    /// <summary>
    /// Gets or sets the logger factory for diagnostic logging.
    /// When null, logging is disabled (NullLoggerFactory is used internally).
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// Creates a copy of these options.
    /// </summary>
    /// <returns>A new ATProtoClientOptions instance with the same values.</returns>
    public ATProtoClientOptions Clone()
    {
        return new ATProtoClientOptions
        {
            BaseUrl = BaseUrl,
            HttpClient = HttpClient,
            TokenProvider = TokenProvider,
            IdentityResolver = IdentityResolver,
            JsonOptions = JsonOptions,
            CborContext = CborContext,
            LabelerDids = LabelerDids,
            Timeout = Timeout,
            AutoRetryOnAuthFailure = AutoRetryOnAuthFailure,
            CreateIdentityResolver = CreateIdentityResolver,
            EnableRateLimitHandler = EnableRateLimitHandler,
            AutoRetryOnRateLimit = AutoRetryOnRateLimit,
            RateLimitMaxRetries = RateLimitMaxRetries,
            UserAgent = UserAgent,
            SessionStore = SessionStore,
            LoggerFactory = LoggerFactory
        };
    }
}
