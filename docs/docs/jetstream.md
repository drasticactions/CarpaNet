# Jetstream

Jetstream provides a lightweight, JSON-based WebSocket event stream. Requires the `CarpaNet.Jetstream` package.
This is useful when you only care for a type of collection that you want to respond to. It also uses far less data than the full Firehose.

```csharp
using CarpaNet.Jetstream;

using var client = new JetstreamClient(
    new Uri("https://jetstream1.us-east.bsky.network"));

var options = new JetstreamSubscribeOptions
{
    WantedCollections = new[] { "app.bsky.feed.post", "app.bsky.feed.like" },
    WantedDids = new[] { "did:plc:z72i7hdynmk6r22z27h6tvur" },  // optional, max 10,000
    Cursor = 1725911162329308,   // optional, resume from Unix microsecond timestamp
    Compress = true,             // enable zstd compression
};

await foreach (var evt in client.SubscribeAsync(options))
{
    switch (evt.Kind)
    {
        case "commit" when evt.Commit is { } commit:
            Console.WriteLine($"[{commit.Operation}] {commit.Collection}/{commit.Rkey}");
            if (commit.Record is { } record)
            {
                // record is a JsonElement — parse with your generated types
                var type = record.TryGetProperty("$type", out var t) ? t.GetString() : null;
                Console.WriteLine($"  $type={type}");
            }
            break;

        case "identity" when evt.Identity is { } identity:
            Console.WriteLine($"[Identity] {evt.Did} → {identity.Handle}");
            break;

        case "account" when evt.Account is { } account:
            Console.WriteLine($"[Account] {evt.Did} active={account.Active} status={account.Status}");
            break;
    }
}
```

## Dynamic Filter Updates

```csharp
await client.SendOptionsUpdateAsync(new JetstreamOptionsUpdate
{
    WantedCollections = new[] { "app.bsky.graph.follow" },
});
```
