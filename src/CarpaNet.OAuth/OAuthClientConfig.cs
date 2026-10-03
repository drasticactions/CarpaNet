using System;
using System.Net.Http;
using System.Text.Json;
using CarpaNet.Identity;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Scopes;
using CarpaNet.OAuth.Storage;
using Microsoft.Extensions.Logging;

namespace CarpaNet.OAuth;

/// <summary>
/// Configuration for the ATProto OAuth client.
/// </summary>
public sealed class OAuthClientConfig
{
    /// <summary>
    /// The client ID. For web apps, this is the URL where client metadata is hosted.
    /// For loopback clients, use <see cref="CreateLoopbackClientId(int, string)"/> or <see cref="CreateLoopback"/>.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The redirect URI to use for OAuth callbacks.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// The scope to request (default: "atproto"), as a space-separated string.
    /// Use <see cref="SetScope(ScopeSet)"/> to build it from a <see cref="ScopeSet"/>.
    /// </summary>
    public string Scope { get; set; } = "atproto";

    /// <summary>
    /// Sets <see cref="Scope"/> from a <see cref="ScopeSet"/>.
    /// </summary>
    /// <param name="scopes">The scopes to request. Must contain <c>atproto</c>.</param>
    /// <returns>This configuration, for chaining.</returns>
    /// <exception cref="ArgumentException">The set does not contain the <c>atproto</c> scope.</exception>
    public OAuthClientConfig SetScope(ScopeSet scopes)
    {
        if (scopes == null)
        {
            throw new ArgumentNullException(nameof(scopes));
        }

        if (!scopes.Contains(AtprotoScope.Atproto))
        {
            throw new ArgumentException("atproto OAuth requires the 'atproto' scope.", nameof(scopes));
        }

        Scope = scopes.ToString();
        return this;
    }

    /// <summary>
    /// The HttpClient to use for requests. If not provided, a new one will be created.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// The state store for OAuth authorization state. If not provided, an in-memory store will be used.
    /// </summary>
    public IOAuthStateStore? StateStore { get; set; }

    /// <summary>
    /// The session store for OAuth sessions. If not provided, an in-memory store will be used.
    /// </summary>
    public IOAuthSessionStore? SessionStore { get; set; }

    /// <summary>
    /// The private key for client authentication (if using private_key_jwt).
    /// </summary>
    public DPoPKeyPair? ClientKey { get; set; }

    /// <summary>
    /// The state expiration time (default: 10 minutes).
    /// </summary>
    public TimeSpan StateExpiration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The token refresh buffer (default: 30 seconds).
    /// Tokens will be refreshed this amount of time before they expire.
    /// </summary>
    public TimeSpan RefreshBuffer { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The JSON serializer options to use for request/response serialization.
    /// Should include a source-generated IJsonTypeInfoResolver for AOT compatibility.
    /// If not provided, a reflection-based fallback will be used.
    /// </summary>
    public JsonSerializerOptions? JsonOptions { get; set; }

    /// <summary>
    /// Gets or sets the list of labeler DIDs whose labels should be included in responses.
    /// When set, the atproto-accept-labelers header is added to requests.
    /// </summary>
    public IReadOnlyList<string>? LabelerDids { get; set; }

    /// <summary>
    /// Gets or sets the logger factory for diagnostic logging.
    /// When null, logging is disabled (NullLoggerFactory is used internally).
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// Gets or sets the identity resolver for handle/DID resolution.
    /// If null, a new resolver will be created if needed.
    /// </summary>
    public IdentityResolver? IdentityResolver { get; set; }

    /// <summary>
    /// Creates an atproto loopback client ID for a native/desktop application listening on
    /// <see cref="CreateLoopbackRedirectUri"/>: <c>http://localhost?redirect_uri=http%3A%2F%2F127.0.0.1%3A{port}%2Fcallback</c>.
    /// The client may only request the default <c>atproto</c> scope; use
    /// <see cref="CreateLoopbackClientId(int, string)"/> or <see cref="AtprotoLoopbackClientId.Build"/> for other scopes.
    /// </summary>
    /// <param name="port">The local port for the callback server.</param>
    /// <returns>A loopback client ID.</returns>
    public static string CreateLoopbackClientId(int port)
    {
        return CreateLoopbackClientId(port, scope: null);
    }

    /// <summary>
    /// Creates an atproto loopback client ID for a native/desktop application listening on
    /// <see cref="CreateLoopbackRedirectUri"/>. The client ID is <c>http://localhost</c> with the
    /// <c>redirect_uri</c> query parameter and, when it is not <c>atproto</c>, the <c>scope</c> parameter.
    /// The authorization server only lets the client request scopes listed in its client ID, so
    /// <paramref name="scope"/> should match <see cref="Scope"/>.
    /// </summary>
    /// <param name="port">The local port for the callback server.</param>
    /// <param name="scope">The scope the client will request, or null for <c>atproto</c>. Must contain <c>atproto</c>.</param>
    /// <returns>A loopback client ID.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The port is not between 1 and 65535.</exception>
    /// <exception cref="ArgumentException">The scope is invalid or does not contain <c>atproto</c>.</exception>
    public static string CreateLoopbackClientId(int port, string? scope)
    {
        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");
        }

        return AtprotoLoopbackClientId.Build(scope, new[] { CreateLoopbackRedirectUri(port) });
    }

    /// <summary>
    /// Creates a configuration for a native/desktop application using an atproto loopback client ID:
    /// sets <see cref="ClientId"/>, <see cref="RedirectUri"/> (<c>http://127.0.0.1:{port}/callback</c>)
    /// and <see cref="Scope"/> consistently.
    /// </summary>
    /// <param name="port">The local port for the callback server.</param>
    /// <param name="scope">The scope to request (default <c>atproto</c>). Must contain <c>atproto</c>.</param>
    /// <returns>A new configuration.</returns>
    public static OAuthClientConfig CreateLoopback(int port, string scope = AtprotoLoopbackClientId.DefaultScope)
    {
        return new OAuthClientConfig
        {
            ClientId = CreateLoopbackClientId(port, scope),
            RedirectUri = CreateLoopbackRedirectUri(port),
            Scope = scope,
        };
    }

    /// <summary>
    /// Creates a loopback redirect URI.
    /// </summary>
    /// <param name="port">The local port for the callback server.</param>
    /// <returns>A loopback redirect URI.</returns>
    public static string CreateLoopbackRedirectUri(int port)
    {
        return $"http://127.0.0.1:{port}/callback";
    }
}
