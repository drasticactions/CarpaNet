using System;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.Storage;

/// <summary>
/// Represents stored password-based session data.
/// </summary>
public sealed class SessionData
{
    /// <summary>
    /// The access JWT token.
    /// </summary>
    public string AccessJwt { get; set; } = string.Empty;

    /// <summary>
    /// The refresh JWT token.
    /// </summary>
    public string RefreshJwt { get; set; } = string.Empty;

    /// <summary>
    /// The user's DID.
    /// </summary>
    public string Did { get; set; } = string.Empty;

    /// <summary>
    /// The user's handle.
    /// </summary>
    public string? Handle { get; set; }

    /// <summary>
    /// The PDS URL.
    /// </summary>
    public string PdsUrl { get; set; } = string.Empty;
}

/// <summary>
/// Storage for password-based sessions (after successful authentication).
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Stores a session for a user.
    /// </summary>
    /// <param name="sub">The user's DID (subject).</param>
    /// <param name="data">The session data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(string sub, SessionData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a session for a user.
    /// </summary>
    /// <param name="sub">The user's DID (subject).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session data if found, null otherwise.</returns>
    Task<SessionData?> GetAsync(string sub, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a session for a user.
    /// </summary>
    /// <param name="sub">The user's DID (subject).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string sub, CancellationToken cancellationToken = default);
}
