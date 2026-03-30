# Common Operations

## Get a Profile

```csharp
var profile = await client.AppBskyActorGetProfileAsync(
    new AppBsky.Actor.GetProfileParameters { Actor = new ATHandle("alice.bsky.social") });
```

## Create a Post

```csharp
var post = new AppBsky.Feed.Post
{
    Text = "Hello from CarpaNet!",
    CreatedAt = DateTimeOffset.UtcNow,
};

var result = await client.ComAtprotoRepoCreateRecordAsync(
    new ComAtproto.Repo.CreateRecordInput
    {
        Repo = new ATIdentifier(client.AuthenticatedDid!),
        Collection = AppBsky.Feed.Post.RecordType,
        Record = post.ToJson(),
    });

Console.WriteLine($"Posted: {result.Uri}");
```

## Delete a Record

```csharp
var atUri = new ATUri(result.Uri.Value);

await client.ComAtprotoRepoDeleteRecordAsync(
    new ComAtproto.Repo.DeleteRecordInput
    {
        Repo = new ATIdentifier(client.AuthenticatedDid!),
        Collection = AppBsky.Feed.Post.RecordType,
        Rkey = atUri.RecordKey!,
    });
```

## List Records

```csharp
var records = await client.ComAtprotoRepoListRecordsAsync(
    new ComAtproto.Repo.ListRecordsParameters
    {
        Repo = "did:plc:example",
        Collection = AppBsky.Feed.Post.RecordType,
        Limit = 50,
    });

foreach (var record in records.Records)
{
    var post = AppBsky.Feed.Post.FromJson(record.Value);
    Console.WriteLine(post?.Text);
}
```

## Get a Timeline

```csharp
var timeline = await client.AppBskyFeedGetTimelineAsync(
    new AppBsky.Feed.GetTimelineParameters { Limit = 25 });

foreach (var item in timeline.Feed)
{
    Console.WriteLine($"{item.Post.Author.Handle}: {item.Post.Record}");
}
```

## AT Protocol Types

CarpaNet provides strongly-typed wrappers for AT Protocol identifiers:

```csharp
// DID
var did = new ATDid("did:plc:z72i7hdynmk6r22z27h6tvur");
Console.WriteLine(did.Method);  // "plc"

// Handle
var handle = new ATHandle("alice.bsky.social");

// AT URI
var uri = ATUri.Create("did:plc:example", "app.bsky.feed.post", "3k2la7k");
Console.WriteLine(uri.Collection);  // "app.bsky.feed.post"
Console.WriteLine(uri.RecordKey);   // "3k2la7k"

// AT Identifier (accepts either DID or Handle)
var id = new ATIdentifier("alice.bsky.social");
Console.WriteLine(id.IsHandle);  // true

// CID
var cid = ATCid.FromSha256Hash(sha256Bytes);
```

All identifier types support equality, implicit string conversion, and JSON serialization.
