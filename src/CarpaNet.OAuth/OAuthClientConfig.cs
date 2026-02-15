using System;
using System.Net.Http;
using System.Text.Json;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;

namespace CarpaNet.OAuth;

/// <summary>
/// Configuration for the ATProto OAuth client.
/// </summary>
public sealed class OAuthClientConfig
{
    /// <summary>
    /// The client ID. For web apps, this is the URL where client metadata is hosted.
    /// For loopback clients, use <see cref="CreateLoopbackClientId"/>.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The redirect URI to use for OAuth callbacks.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// The scope to request (default: "atproto").
    /// </summary>
    public string Scope { get; set; } = "atproto";

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
    /// Creates a loopback client ID for native/desktop applications.
    /// </summary>
    /// <param name="port">The local port for the callback server.</param>
    /// <param name="state">Optional state parameter.</param>
    /// <returns>A loopback client ID.</returns>
    public static string CreateLoopbackClientId(int port, string? state = null)
    {
        var stateParam = string.IsNullOrEmpty(state) ? "" : $"&state={Uri.EscapeDataString(state)}";
        return $"http://127.0.0.1:{port}/?{stateParam}";
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
