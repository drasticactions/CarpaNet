using CarpaNet;
using CarpaNet.Jetstream;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine();
    Console.WriteLine("Shutting down...");
};

// Parse arguments
long? cursor = null;
var collections = new List<string>();
var dids = new List<string>();
string endpoint = BlueskyServices.Jetstream1UsEast;
bool compress = false;
string? zstdDictionaryPath = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--cursor" when i + 1 < args.Length && long.TryParse(args[i + 1], out var c):
            cursor = c;
            i++;
            break;
        case "--collection" when i + 1 < args.Length:
            collections.Add(args[i + 1]);
            i++;
            break;
        case "--did" when i + 1 < args.Length:
            dids.Add(args[i + 1]);
            i++;
            break;
        case "--endpoint" when i + 1 < args.Length:
            endpoint = args[i + 1];
            i++;
            break;
        case "--compress":
            compress = true;
            break;
        case "--zstd-dictionary" when i + 1 < args.Length:
            zstdDictionaryPath = args[i + 1];
            i++;
            break;
    }
}

Console.WriteLine("=== CarpaNet Jetstream Test ===");
Console.WriteLine($"Connecting to {endpoint}...");
if (cursor.HasValue)
{
    Console.WriteLine($"Resuming from cursor: {cursor.Value}");
}

if (collections.Count > 0)
{
    Console.WriteLine($"Filtering collections: {string.Join(", ", collections)}");
}

if (dids.Count > 0)
{
    Console.WriteLine($"Filtering DIDs: {string.Join(", ", dids)}");
}

if (compress)
{
    Console.WriteLine("Compression enabled");
}

Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

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
            case "commit" when evt.Commit != null:
                var commit = evt.Commit;
                Console.WriteLine($"[Commit] {commit.Operation} {commit.Collection}/{commit.Rkey} from {evt.Did}");
                if (commit.Record.HasValue)
                {
                    var recordType = commit.Record.Value.TryGetProperty("$type", out var typeEl)
                        ? typeEl.GetString()
                        : null;
                    Console.WriteLine($"  record $type={recordType}");
                }

                break;

            case "identity" when evt.Identity != null:
                Console.WriteLine($"[Identity] did={evt.Did} handle={evt.Identity.Handle}");
                break;

            case "account" when evt.Account != null:
                Console.WriteLine($"[Account] did={evt.Did} active={evt.Account.Active} status={evt.Account.Status}");
                break;

            default:
                Console.WriteLine($"[{evt.Kind}] did={evt.Did}");
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

Console.WriteLine("Jetstream stream ended.");
