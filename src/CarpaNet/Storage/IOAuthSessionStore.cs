using System;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.OAuth.Crypto;

namespace CarpaNet.OAuth.Storage;

/// <summary>
/// Represents stored OAuth session data.
/// </summary>
public sealed class OAuthSessionData
{
    /// <summary>
    /// The DPoP key pair (serialized as JWK with private key).
    /// </summary>
    public JsonWebKey DPoPKey { get; set; } = new();

    /// <summary>
    /// The client authentication method used.
    /// </summary>
    public string? AuthMethod { get; set; }

    /// <summary>
    /// The token set.
    /// </summary>
    public TokenSet TokenSet { get; set; } = new();
}

/// <summary>
/// Storage for OAuth sessions (after successful authentication).
/// </summary>
public interface IOAuthSessionStore
{
    /// <summary>
    /// Stores a session for a user.
    /// </summary>
    /// <param name="sub">The user's DID (subject).</param>
    /// <param name="data">The session data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(string sub, OAuthSessionData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a session for a user.
    /// </summary>
    /// <param name="sub">The user's DID (subject).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session data if found, null otherwise.</returns>
    Task<OAuthSessionData?> GetAsync(string sub, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a session for a user.
    /// </summary>
    /// <param name="sub">The user's DID (subject).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string sub, CancellationToken cancellationToken = default);
}
