# JetstreamTest

A sample console application that connects to a Bluesky Jetstream instance and streams real-time events as JSON.

## Prerequisites

- .NET 10 SDK

## Running

```bash
# Run with defaults (jetstream1.us-east.bsky.network, all events)
dotnet run --project samples/JetstreamTest

# Filter to only posts
dotnet run --project samples/JetstreamTest -- --collection app.bsky.feed.post

# Filter to multiple collections
dotnet run --project samples/JetstreamTest -- --collection app.bsky.feed.post --collection app.bsky.feed.like

# Filter to a specific DID
dotnet run --project samples/JetstreamTest -- --did did:plc:example123

# Use a different Jetstream instance
dotnet run --project samples/JetstreamTest -- --endpoint https://jetstream2.us-west.bsky.network

# Resume from a cursor (unix microseconds)
dotnet run --project samples/JetstreamTest -- --cursor 1725911162329308

# Enable zstd compression (requires dictionary file)
dotnet run --project samples/JetstreamTest -- --compress --zstd-dictionary /path/to/zstd_dictionary
```

The zstd dictionary can be obtained from the Jetstream repository:
https://github.com/bluesky-social/jetstream/raw/main/pkg/models/zstd_dictionary

Press `Ctrl+C` to stop the stream.

## What it demonstrates

- Creating a `JetstreamClient` pointed at a public Jetstream instance
- Subscribing with `JetstreamSubscribeOptions` for collection/DID filtering and cursor-based resumption
- Pattern matching on event kinds (`commit`, `identity`, `account`)
- Accessing decoded record data as `JsonElement` from commit events
- Optional zstd compression with a user-provided dictionary
- Graceful cancellation with `CancellationToken`

## Jetstream vs Firehose

Jetstream is a lightweight alternative to the full ATProtocol firehose (`com.atproto.sync.subscribeRepos`):
- JSON instead of CBOR — no CAR/MST parsing needed
- One event per record operation instead of batched commits
- Server-side filtering by collection and DID
- Time-based cursors (portable across instances)
