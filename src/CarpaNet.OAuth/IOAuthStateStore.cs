using System;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.OAuth.Crypto;

namespace CarpaNet.OAuth.Storage;

/// <summary>
/// Represents the state data stored during an OAuth authorization flow.
/// </summary>
public sealed class OAuthStateData
{
    /// <summary>
    /// The authorization server issuer.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// The DPoP key pair (serialized as JWK).
    /// </summary>
    public JsonWebKey DPoPKey { get; set; } = new();

    /// <summary>
    /// The PKCE code verifier.
    /// </summary>
    public string Verifier { get; set; } = string.Empty;

    /// <summary>
    /// The client authentication method.
    /// </summary>
    public string? AuthMethod { get; set; }

    /// <summary>
    /// Optional application-defined state.
    /// </summary>
    public string? AppState { get; set; }

    /// <summary>
    /// The PDS URL for the resource server.
    /// </summary>
    public string? PdsUrl { get; set; }

    /// <summary>
    /// When this state expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Storage for OAuth authorization state (during auth flow).
/// </summary>
public interface IOAuthStateStore
{
    /// <summary>
    /// Stores state data for an authorization flow.
    /// </summary>
    /// <param name="state">The state parameter (key).</param>
    /// <param name="data">The state data to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(string state, OAuthStateData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves and deletes state data (atomically to prevent replay).
    /// </summary>
    /// <param name="state">The state parameter (key).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The state data if found and not expired, null otherwise.</returns>
    Task<OAuthStateData?> ConsumeAsync(string state, CancellationToken cancellationToken = default);
}
