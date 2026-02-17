using CarpaNet;
using ComAtproto.Repo;
using ComWhtwnd.Blog;

Console.WriteLine("=== CarpaNet Remote Lexicon Resolution Test ===");

var atProto = ATProtoClientFactory.Create();

var listRecordsParamter = new ListRecordsParameters()
{
    Repo = "did:plc:ilxxgyz7oz7mysber4omeqrg",
    Collection = Entry.RecordType,
    Limit = 1
};

var result = await atProto.ComAtprotoRepoListRecordsAsync(listRecordsParamter);

foreach (var record in result.Records)
{
    Console.WriteLine($"Record URI: {record.Uri}");
    Console.WriteLine($"Record CID: {record.Cid}");
    Console.WriteLine($"Record Value: {record.Value}");

    var entry = Entry.FromJson(record.Value);
    if (entry is null)
    {
        Console.WriteLine("Failed to parse record value as Entry.");
    }
    else
    {
        Console.WriteLine($"Parsed Entry Title: {entry.Title}");
    }
}