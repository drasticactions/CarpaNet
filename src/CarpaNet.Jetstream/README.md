# CarpaNet.Jetstream

[![NuGet Version](https://img.shields.io/nuget/v/CarpaNet.Jetstream.svg)](https://www.nuget.org/packages/CarpaNet.Jetstream/) ![License](https://img.shields.io/badge/License-MIT-blue.svg)

![CarpaNet Logo](https://user-images.githubusercontent.com/898335/253740405-4b0ae177-cc49-4c26-b6b0-ab8e835a0e62.png)

CarpaNet.Jetstream lets you connect to a Bluesky Jetstream instance.

![1444070256569233](https://user-images.githubusercontent.com/898335/167266846-1ad2648f-91c1-4a04-a18d-6dd4d6c7d21c.gif)

This library is experimental and not stable. Expect issues and bugs!

The package contains two clients:

- **`JetstreamV2Client`** — the current Jetstream service ([bluesky-social/jetstream](https://github.com/bluesky-social/jetstream)): the `network.bsky.jetstream.subscribeEvents` live websocket plus the sealed-archive replay API. Use this for new code.
- **`JetstreamClient`** — the legacy v1 `/subscribe` wire.

# Jetstream v2

`SubscribeAsync` is a managed stream: automatic reconnect with backoff, seq-based resume and dedup, optional dict-zstd compression with automatic dictionary fetch/rotation, and seamless archive-backfill-to-live cutover.

```csharp
using var client = new JetstreamV2Client(
    new Uri(BlueskyServices.JetstreamUsEast),
    new JetstreamV2ClientOptions { EnableCompression = true });

var options = new JetstreamV2SubscribeOptions
{
    Collections = new[] { "app.bsky.feed.post" },  // exact NSIDs or wildcards like app.bsky.feed.*
    // LiveCursor = lastSeq,   // resume a live tail from a saved seq
    // AfterSeq = 0,           // or: replay the whole sealed archive first, then go live
    // SnapshotOnly = true,    // or: archive dump only, no websocket
};

await foreach (var evt in client.SubscribeAsync(options, cts.Token))
{
    if (evt.Kind == JetstreamV2EventKind.Commit && evt.Commit is { } commit)
    {
        Console.WriteLine($"[{commit.Operation}] seq={evt.Seq} {commit.Collection}/{commit.Rkey}");
        // Typed records via your generated context:
        // var post = commit.GetRecord(ATProtoJsonContext.Default.AppBskyFeedPost);
    }
}
```

Persist the last `evt.Seq` and pass it back (`LiveCursor` for live, `AfterSeq` for replay) to resume. Delivery is at-least-once — fold idempotently or dedup by seq. The archive endpoints (`PlanSnapshotAsync`, `GetSegmentAsync`, `GetBlockAsync`, plus the `JetstreamSegmentFormat` decoder) are also available directly.

# Jetstream v1 (legacy)

```csharp

byte[]? zstdDictionary = null;
if (zstdDictionaryPath != null)
{
    zstdDictionary = File.ReadAllBytes(zstdDictionaryPath);
    Console.WriteLine($"Loaded zstd dictionary from {zstdDictionaryPath} ({zstdDictionary.Length} bytes)");
}
else if (compress)
{
    Console.WriteLine("Warning: --compress specified without --zstd-dictionary. Binary frames will fail to decompress.");
}

using var client = new JetstreamClient(new Uri(endpoint), zstdDictionary);

var options = new JetstreamSubscribeOptions
{
    Cursor = cursor,
    WantedCollections = collections.Count > 0 ? collections : null,
    WantedDids = dids.Count > 0 ? dids : null,
    Compress = compress,
};

try
{
    await foreach (var evt in client.SubscribeAsync(options, cts.Token))
    {
        switch (evt.Kind)
        {
            // ... switch on events...
        }
    }
}
```