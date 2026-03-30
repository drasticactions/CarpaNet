# Firehose

Subscribe to the ATProtocol firehose for CBOR-encoded, batched commit events. Uses the core CarpaNet package — add `com.atproto.sync.subscribeRepos` to your lexicon resolves.

```csharp
var client = ATProtoClientFactory.Create(new ATProtoClientOptions
{
    BaseUrl = new Uri("https://bsky.network"),  // relay URL
});

await foreach (var message in client.ComAtprotoSyncSubscribeReposAsync(cancellationToken: cts.Token))
{
    switch (message)
    {
        case ComAtproto.Sync.SubscribeReposCommit commit:
            Console.WriteLine($"[Commit] seq={commit.Seq} repo={commit.Repo}");
            foreach (var op in commit.Ops ?? [])
            {
                Console.WriteLine($"  {op.Action} {op.Path}");
            }
            break;

        case ComAtproto.Sync.SubscribeReposIdentity identity:
            Console.WriteLine($"[Identity] {identity.Did} → {identity.Handle}");
            break;

        case ComAtproto.Sync.SubscribeReposAccount account:
            Console.WriteLine($"[Account] {account.Did} active={account.Active}");
            break;
    }
}
```
