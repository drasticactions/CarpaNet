using CarpaNet;
using ComAtproto.Sync;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine();
    Console.WriteLine("Shutting down...");
};

long? cursor = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--cursor" && i + 1 < args.Length && long.TryParse(args[i + 1], out var c))
    {
        cursor = c;
        break;
    }
}

Console.WriteLine("=== CarpaNet Firehose Test ===");
Console.WriteLine($"Connecting to {BlueskyServices.Relay}...");
if (cursor.HasValue)
{
    Console.WriteLine($"Resuming from cursor: {cursor.Value}");
}

Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

var client = ATProtoClientFactory.CreatePublicClient(baseUrl: new Uri(BlueskyServices.Relay));

var parameters = cursor.HasValue ? new SubscribeReposParameters { Cursor = cursor.Value } : null;

try
{
    await foreach (var message in client.ComAtprotoSyncSubscribeReposAsync(parameters, cts.Token))
    {
        switch (message)
        {
            case SubscribeReposCommit commit:
                Console.WriteLine($"[Commit] seq={commit.Seq} repo={commit.Repo} rev={commit.Rev} ops={commit.Ops?.Count}");
                if (commit.Ops != null)
                {
                    foreach (var op in commit.Ops)
                    {
                        Console.WriteLine($"  {op.Action} {op.Path}");
                    }
                }

                break;

            case SubscribeReposIdentity identity:
                Console.WriteLine($"[Identity] seq={identity.Seq} did={identity.Did} handle={identity.Handle}");
                break;

            case SubscribeReposAccount account:
                Console.WriteLine($"[Account] seq={account.Seq} did={account.Did} active={account.Active} status={account.Status}");
                break;

            case SubscribeReposSync sync:
                Console.WriteLine($"[Sync] seq={sync.Seq} did={sync.Did} rev={sync.Rev}");
                break;

            case SubscribeReposInfo info:
                Console.WriteLine($"[Info] name={info.Name} message={info.Message}");
                break;

            default:
                Console.WriteLine($"[Unknown] {message.GetType().Name}");
                break;
        }
    }
}
catch (OperationCanceledException)
{
    // Expected on Ctrl+C
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

Console.WriteLine("Firehose stream ended.");
