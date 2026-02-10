# FirehoseTest

A sample console application that connects to the ATProtocol firehose (relay) and streams real-time events.

## Prerequisites

- .NET 10 SDK
- ATProtocol Lexicon JSON files (set `ATPROTO_LEXICON` environment variable to the directory path)

## Running

```bash
# Set the lexicon path
export ATPROTO_LEXICON=/path/to/atproto/lexicons

# Run the sample
dotnet run --project samples/FirehoseTest

# Resume from a specific sequence number
dotnet run --project samples/FirehoseTest -- --cursor 12345
```

Press `Ctrl+C` to stop the stream.

## What it demonstrates

- Creating a public (unauthenticated) client pointed at the Bluesky relay (`bsky.network`)
- Subscribing to `com.atproto.sync.subscribeRepos` via the generated `ComAtprotoSyncSubscribeReposAsync` extension method
- Pattern matching on the discriminated union message types (`Commit`, `Identity`, `Account`, `Sync`, `Info`)
- Using cursor-based resumption to pick up from a specific sequence number
- Graceful cancellation with `CancellationToken`
