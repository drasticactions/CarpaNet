using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.OAuth.Storage;

/// <summary>
/// In-memory implementation of <see cref="IOAuthStateStore"/>.
/// </summary>
public sealed class MemoryOAuthStateStore : IOAuthStateStore
{
    private readonly ConcurrentDictionary<string, OAuthStateData> _store = new();

    /// <inheritdoc/>
    public Task StoreAsync(string state, OAuthStateData data, CancellationToken cancellationToken = default)
    {
        _store[state] = data;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<OAuthStateData?> ConsumeAsync(string state, CancellationToken cancellationToken = default)
    {
        if (_store.TryRemove(state, out var data))
        {
            // Check if expired
            if (data.ExpiresAt < DateTimeOffset.UtcNow)
            {
                return Task.FromResult<OAuthStateData?>(null);
            }

            return Task.FromResult<OAuthStateData?>(data);
        }

        return Task.FromResult<OAuthStateData?>(null);
    }
}
