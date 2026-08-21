# JetstreamV2Test

Demonstrates `JetstreamV2Client`: the managed Jetstream v2 stream that welds sealed-archive
replay (planSnapshot → getSegment/getBlock over HTTP) and the live
`network.bsky.jetstream.subscribeEvents` websocket into one seq-ordered event stream with
automatic reconnect, resume, and compression handling.

## Usage

```bash
# Live tail from the current tip
dotnet run

# Live tail, filtered, with dict-zstd compression (dictionary fetched automatically)
dotnet run -- --collection app.bsky.feed.post --compress

# Resume a live tail from a saved seq
dotnet run -- --cursor 24959600000

# Replay sealed history from a seq, then cut over to live with no gap
dotnet run -- --after-seq 24959600000 --collection app.bsky.feed.post

# Point-in-time archive snapshot (no websocket)
dotnet run -- --after-seq 1000000 --before-seq 1000400 --snapshot-only
```

Options:

| Flag | Meaning |
| --- | --- |
| `--collection <nsid>` | Filter commits to a collection (repeatable; wildcards like `app.bsky.feed.*`) |
| `--did <did>` | Filter to a repo DID (repeatable) |
| `--kind <kind>` | Filter to `commit`, `identity`, `account`, or `sync` (repeatable) |
| `--cursor <seq>` | Resume a pure live tail from a saved seq |
| `--after-seq <seq>` | Replay the sealed archive after this seq, then go live (0 = everything) |
| `--before-seq <seq>` | Snapshot upper bound (requires `--snapshot-only`) |
| `--snapshot-only` | Archive dump only; the stream ends at the sealed tip |
| `--compress` | Enable dict-zstd live-tail compression |
| `--endpoint <url>` | Jetstream v2 instance (default: `jetstream.us-east.bsky.network`) |
| `--api-key <key>` | Archive API key (also read from `JETSTREAM_CLIENT_API_KEY`) |
| `--verbose` | Show client diagnostics (reconnects, sweeps, degraded compression) |

The final line prints the last delivered seq — pass it back with `--cursor` (live) or
`--after-seq` (replay) to resume. Delivery is at-least-once across restarts; consumers fold
idempotently or dedup by seq.

## Jetstream v2 vs v1

The v2 wire (`JetstreamV2Client`) adds a durable per-event `seq` cursor, an archive you can
replay from seq 0, a `sync` event kind, and self-describing frames. The v1 `/subscribe` wire
(`JetstreamClient`, see the `JetstreamTest` sample) remains for legacy servers.
