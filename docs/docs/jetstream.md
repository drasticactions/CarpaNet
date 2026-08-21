# Jetstream

Jetstream provides a lightweight, JSON-based WebSocket event stream. Requires the `CarpaNet.Jetstream` package.
This is useful when you only care for a type of collection that you want to respond to. It also uses far less data than the full Firehose.

The package contains two clients:

- **`JetstreamV2Client`** — the current Jetstream service ([bluesky-social/jetstream](https://github.com/bluesky-social/jetstream)): the `network.bsky.jetstream.subscribeEvents` websocket plus a full-network sealed archive you can replay over HTTP. Use this for new code.
- **`JetstreamClient`** — the legacy v1 `/subscribe` wire, kept for servers that only expose it.

## Jetstream v2

`JetstreamV2Client.SubscribeAsync` is a managed stream: it reconnects with exponential backoff, resumes at the last delivered seq, deduplicates the at-least-once overlap, fetches and rotates the zstd compression dictionary, and, when you ask for history, downloads the sealed archive over HTTP and cuts over to the live tail with no gap.

```csharp
using CarpaNet;
using CarpaNet.Jetstream;

using var client = new JetstreamV2Client(
    new Uri(BlueskyServices.JetstreamUsEast),
    new JetstreamV2ClientOptions
    {
        EnableCompression = true,   // dict-zstd; the dictionary is fetched automatically
    });

var options = new JetstreamV2SubscribeOptions
{
    Collections = new[] { "app.bsky.feed.post" },  // exact NSIDs or wildcards like app.bsky.feed.*
    // LiveCursor = lastSeq,     // resume a live tail from a saved seq
    // AfterSeq = 0,             // or: replay the whole archive first, then go live
    // SnapshotOnly = true,      // or: archive dump only, no websocket
};

await foreach (var evt in client.SubscribeAsync(options))
{
    switch (evt.Kind)
    {
        case JetstreamV2EventKind.Commit when evt.Commit is { } commit:
            Console.WriteLine($"[{commit.Operation}] seq={evt.Seq} {commit.Collection}/{commit.Rkey}");
            // Typed records via your generated JSON context:
            // var post = commit.GetRecord(ATProtoJsonContext.Default.AppBskyFeedPost);
            break;

        case JetstreamV2EventKind.Identity when evt.Identity is { } identity:
            Console.WriteLine($"[Identity] {evt.Did} → {identity.Handle}");
            break;

        case JetstreamV2EventKind.Account when evt.Account is { } account:
            Console.WriteLine($"[Account] {evt.Did} active={account.Active} status={account.Status}");
            break;

        case JetstreamV2EventKind.Sync when evt.Sync is { } sync:
            Console.WriteLine($"[Sync] {evt.Did} rev={sync.Rev}");
            break;
    }
}
```

Things worth knowing:

- `evt.Seq` is the cursor: Persist the last seen value and pass it back as `LiveCursor` (pure live) or `AfterSeq` (archive replay) to resume. Delivery is at-least-once across restarts — fold idempotently or dedup by seq.
- Collection filters never suppress `#identity`/`#account`/`#sync`: those DID-level events are your only signal to purge a deleted account's records. Ask for `Kinds = [JetstreamV2EventKind.Commit]` explicitly if you want commits only.
- Filters are immutable per connection on the v2 wire; there is no `options_update`. Start a new subscription to change them.
- `CursorTooOld`: on a backfill-enabled stream the client automatically re-enters archive replay; on a pure live tail it throws `JetstreamV2Exception` — resume with `AfterSeq` instead.
- The archive endpoints (`PlanSnapshotAsync`, `GetSegmentAsync`, `GetBlockAsync`, decoded via `JetstreamSegmentFormat`) are also public for direct use, and `JetstreamV2ClientOptions.ApiKey` sends a bearer key on them (never on the public websocket or dictionary fetch).

## Jetstream v1 (legacy)

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

### Dynamic Filter Updates (v1 only)

```csharp
await client.SendOptionsUpdateAsync(new JetstreamOptionsUpdate
{
    Payload = new JetstreamOptionsPayload
    {
        WantedCollections = new List<string> { "app.bsky.graph.follow" },
    },
});
```
