using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.Storage;

/// <summary>
/// In-memory implementation of <see cref="ISessionStore"/>.
/// </summary>
public sealed class MemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionData> _store = new();

    /// <inheritdoc/>
    public Task StoreAsync(string sub, SessionData data, CancellationToken cancellationToken = default)
    {
        _store[sub] = data;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<SessionData?> GetAsync(string sub, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(sub, out var data);
        return Task.FromResult<SessionData?>(data);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string sub, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(sub, out _);
        return Task.CompletedTask;
    }
}
