using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.OAuth.Storage;

/// <summary>
/// In-memory implementation of <see cref="IOAuthSessionStore"/>.
/// </summary>
public sealed class MemoryOAuthSessionStore : IOAuthSessionStore
{
    private readonly ConcurrentDictionary<string, OAuthSessionData> _store = new();

    /// <inheritdoc/>
    public Task StoreAsync(string sub, OAuthSessionData data, CancellationToken cancellationToken = default)
    {
        _store[sub] = data;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<OAuthSessionData?> GetAsync(string sub, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(sub, out var data);
        return Task.FromResult<OAuthSessionData?>(data);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string sub, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(sub, out _);
        return Task.CompletedTask;
    }
}
