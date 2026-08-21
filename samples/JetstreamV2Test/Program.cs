using CarpaNet;
using CarpaNet.Jetstream;
using Microsoft.Extensions.Logging;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine();
    Console.WriteLine("Shutting down...");
};

// Parse arguments
long? liveCursor = null;
long? afterSeq = null;
long? beforeSeq = null;
bool snapshotOnly = false;
bool compress = false;
bool verbose = false;
var collections = new List<string>();
var dids = new List<string>();
var kinds = new List<JetstreamV2EventKind>();
string endpoint = BlueskyServices.JetstreamUsEast;
string? apiKey = Environment.GetEnvironmentVariable("JETSTREAM_CLIENT_API_KEY");

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--cursor" when i + 1 < args.Length && long.TryParse(args[i + 1], out var c):
            liveCursor = c;
            i++;
            break;
        case "--after-seq" when i + 1 < args.Length && long.TryParse(args[i + 1], out var a):
            afterSeq = a;
            i++;
            break;
        case "--before-seq" when i + 1 < args.Length && long.TryParse(args[i + 1], out var b):
            beforeSeq = b;
            i++;
            break;
        case "--snapshot-only":
            snapshotOnly = true;
            break;
        case "--collection" when i + 1 < args.Length:
            collections.Add(args[i + 1]);
            i++;
            break;
        case "--did" when i + 1 < args.Length:
            dids.Add(args[i + 1]);
            i++;
            break;
        case "--kind" when i + 1 < args.Length && Enum.TryParse<JetstreamV2EventKind>(args[i + 1], ignoreCase: true, out var k):
            kinds.Add(k);
            i++;
            break;
        case "--endpoint" when i + 1 < args.Length:
            endpoint = args[i + 1];
            i++;
            break;
        case "--api-key" when i + 1 < args.Length:
            apiKey = args[i + 1];
            i++;
            break;
        case "--compress":
            compress = true;
            break;
        case "--verbose":
            verbose = true;
            break;
        case "--help":
            Console.WriteLine("""
                CarpaNet Jetstream v2 sample.

                Live tail:          JetstreamV2Test [--cursor <seq>]
                Full replay then live:        JetstreamV2Test --after-seq 0
                Point-in-time snapshot:       JetstreamV2Test --after-seq 0 [--before-seq <seq>] --snapshot-only

                Options:
                  --collection <nsid>   Filter to a collection
                  --did <did>           Filter to a repo DID
                  --kind <kind>         Filter to commit|identity|account|sync
                  --endpoint <url>      Jetstream v2 instance (default: jetstream.us-east.bsky.network)
                  --api-key <key>       Archive API key (or JETSTREAM_CLIENT_API_KEY env var)
                  --compress            Enable dict-zstd live-tail compression
                  --verbose             Enable client diagnostics logging
                """);
            return;
    }
}

Console.WriteLine("=== CarpaNet Jetstream v2 Test ===");
Console.WriteLine($"Endpoint: {endpoint}");
if (afterSeq.HasValue)
{
    Console.WriteLine(snapshotOnly
        ? $"Mode: archive snapshot ({afterSeq}, {(beforeSeq.HasValue ? beforeSeq.ToString() : "sealed tip")}]"
        : $"Mode: backfill from seq {afterSeq}, then live");
}
else
{
    Console.WriteLine(liveCursor.HasValue ? $"Mode: live from cursor {liveCursor}" : "Mode: live from the current tip");
}

Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

using var loggerFactory = verbose
    ? LoggerFactory.Create(builder => builder.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information))
    : null;

using var client = new JetstreamV2Client(new Uri(endpoint), new JetstreamV2ClientOptions
{
    EnableCompression = compress,
    ApiKey = apiKey,
    LoggerFactory = loggerFactory,
});

var options = new JetstreamV2SubscribeOptions
{
    LiveCursor = liveCursor,
    AfterSeq = afterSeq,
    BeforeSeq = beforeSeq,
    SnapshotOnly = snapshotOnly,
    Collections = collections.Count > 0 ? collections : null,
    Dids = dids.Count > 0 ? dids : null,
    Kinds = kinds.Count > 0 ? kinds : null,
};

long count = 0;
long lastSeq = 0;
try
{
    await foreach (var evt in client.SubscribeAsync(options, cts.Token))
    {
        count++;
        lastSeq = evt.Seq;
        switch (evt.Kind)
        {
            case JetstreamV2EventKind.Commit when evt.Commit != null:
                var commit = evt.Commit;
                Console.WriteLine($"[Commit seq={evt.Seq}] {commit.Operation} {commit.Collection}/{commit.Rkey} from {evt.Did}");
                if (commit.Record.HasValue &&
                    commit.Record.Value.TryGetProperty("$type", out var typeEl))
                {
                    Console.WriteLine($"  record $type={typeEl.GetString()} cid={commit.Cid}");
                }

                break;

            case JetstreamV2EventKind.Identity when evt.Identity != null:
                Console.WriteLine($"[Identity seq={evt.Seq}] did={evt.Did} handle={evt.Identity.Handle}");
                break;

            case JetstreamV2EventKind.Account when evt.Account != null:
                Console.WriteLine($"[Account seq={evt.Seq}] did={evt.Did} active={evt.Account.Active} status={evt.Account.Status}");
                break;

            case JetstreamV2EventKind.Sync when evt.Sync != null:
                Console.WriteLine($"[Sync seq={evt.Seq}] did={evt.Did} rev={evt.Sync.Rev}");
                break;
        }
    }
}
catch (OperationCanceledException)
{
    // Expected on Ctrl+C
}
catch (JetstreamV2Exception ex)
{
    Console.WriteLine($"Stream failed{(ex.ErrorName != null ? $" ({ex.ErrorName})" : string.Empty)}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine($"Stream ended after {count} events; last seq {lastSeq} (pass it back with --cursor or --after-seq to resume).");
