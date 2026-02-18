using CarpaNet;
using ComAtproto.Repo;
using ConsoleAppFramework;

var app = ConsoleApp.Create();
app.Add<AppCommands>();
app.Run(args);

/// <summary>
/// App Commands.
/// </summary>
#pragma warning disable SA1649 // File name should match first type name
public class AppCommands
#pragma warning restore SA1649 // File name should match first type name
{
    /// <summary>
    /// Generates a static HTML site based on the provided identifier.
    /// </summary>
    /// <param name="identifier">The identifier for the static site to generate.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Command("generate")]
    public async Task GenerateStaticSite([Argument] string identifier, CancellationToken cancellationToken = default)
    {
        var atIdentifier = new ATIdentifier(identifier);
        if (!atIdentifier.IsValid)
        {
            Console.WriteLine("Invalid identifier.");
            return;
        }

        using var atProtocol = ATProtoClientFactory.Create();
        var recordsParamter = new ListRecordsParameters()
        {
            Repo = atIdentifier,
            Collection = SiteStandard.Document.RecordType,
        };
        Console.WriteLine($"Fetching records for {atIdentifier}...");
        var records = await atProtocol.ComAtprotoRepoListRecordsAsync(recordsParamter, cancellationToken);
        Console.WriteLine($"Fetched {records.Records.Count} records.");
        foreach(var record in records.Records)
        {
            Console.WriteLine($"Record: {record.Uri}");
            var document = SiteStandard.Document.FromJson(record.Value);
            var type = document!.Content!.Value!.TryGetProperty("$type", out var typeProperty) ? typeProperty.GetString() : null;
            switch(type)
            {
                case BlogPckt.Content.TypeId:
                    BlogPckt.Content? blogPost = BlogPckt.Content.FromJson(document.Content.Value);
                    if (blogPost?.Items is null)
                    {
                        Console.WriteLine($"Failed to parse blog post content for record {record.Uri}");
                        continue;
                    }
                    foreach (var item in blogPost.Items)
                    {
                        var itemType = item.TryGetProperty("$type", out var itemTypeProperty) ? itemTypeProperty.GetString() : null;
                        switch(itemType)
                        {
                            default:
                                Console.WriteLine($"Unknown content type '{itemType}' for item in record {record.Uri}");
                                break;
                        }
                    }
                    break;
                default:
                    Console.WriteLine($"Unknown content type '{type}' for record {record.Uri}");
                    break;
            }
        }
    }
}