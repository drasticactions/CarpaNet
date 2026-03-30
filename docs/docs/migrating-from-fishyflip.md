# Migrating from FishyFlip

This guide helps users transition from [FishyFlip](https://github.com/drasticactions/FishyFlip) to CarpaNet. Both libraries target ATProtocol but differ significantly in architecture and API design.

## Package Mapping

| FishyFlip | CarpaNet | Notes |
|-----------|----------|-------|
| `FishyFlip` | `CarpaNet` | Core library |
| `FishyFlip` (built-in) | `CarpaNet.Jetstream` | Jetstream is a separate package |
| `FishyFlip` (built-in) | `CarpaNet.OAuth` | OAuth is a separate package |
| `FishyFlip.AspNetCore` | `CarpaNet.AspNetCore` | ASP.NET Core integration |

## Declaring Lexicons (New Concept)

FishyFlip bundles all Bluesky lexicons — every API method is available immediately. CarpaNet requires you to **declare which lexicons you need** in your `.csproj`. The source generator then produces only the types and methods you use:

```xml
<!-- CarpaNet: declare what you need -->
<ItemGroup>
  <LexiconResolve Include="app.bsky.actor.getProfile" />
  <LexiconResolve Include="app.bsky.feed.post" />
  <LexiconResolve Include="app.bsky.feed.getTimeline" />
  <LexiconResolve Include="com.atproto.repo.createRecord" />
  <LexiconResolve Include="com.atproto.repo.deleteRecord" />
</ItemGroup>
```

To pull in all lexicons from Bluesky at once, use handle resolution:

```xml
<ItemGroup>
  <LexiconResolveHandle Include="atproto-lexicons.bsky.social" />
  <LexiconResolveHandle Include="bsky-lexicons.bsky.social" />
</ItemGroup>
```

## Client Creation

FishyFlip uses a builder pattern that creates an `ATProtocol` instance:

```csharp
// FishyFlip
var protocol = new ATProtocolBuilder()
    .WithInstanceUrl(new Uri("https://bsky.social"))
    .WithUserAgent("MyApp/1.0")
    .WithLogger(logger)
    .EnableAutoRenewSession(true)
    .Build();
```

CarpaNet uses static factory methods or a constructor with options. A source-generated `ATProtoClientFactory` provides pre-configured JSON/CBOR contexts:

```csharp
// CarpaNet — unauthenticated (public AppView)
var client = ATProtoClientFactory.Create();

// CarpaNet — with app password
var client = await ATProtoClient.CreateWithSessionAsync(
    identifier: "alice.bsky.social",
    password: "xxxx-xxxx-xxxx-xxxx",
    options: new ATProtoClientOptions
    {
        JsonOptions = ATProtoJsonContext.DefaultOptions,
        CborContext = ATProtoCborContext.Default,
    });

// CarpaNet — full options
var client = ATProtoClientFactory.Create(new ATProtoClientOptions
{
    UserAgent = "MyApp/1.0",
    LoggerFactory = loggerFactory,
    EnableRateLimitHandler = true,
    AutoRetryOnAuthFailure = true,
});
```

## API Call Patterns

FishyFlip organizes methods into endpoint groups (`protocol.Actor`, `protocol.Feed`, etc.):

```csharp
// FishyFlip — endpoint groups
var (profile, error) = await protocol.Actor.GetProfileAsync(
    ATHandle.Create("alice.bsky.social"));

var (timeline, error) = await protocol.Feed.GetTimelineAsync(limit: 25);

var (result, error) = await protocol.Feed.CreatePostAsync(post);
```

CarpaNet uses flat extension methods on `IATProtoClient`, named after the NSID:

```csharp
// CarpaNet — extension methods
var profile = await client.AppBskyActorGetProfileAsync(
    new AppBsky.Actor.GetProfileParameters { Actor = new ATHandle("alice.bsky.social") });

var timeline = await client.AppBskyFeedGetTimelineAsync(
    new AppBsky.Feed.GetTimelineParameters { Limit = 25 });

var result = await client.ComAtprotoRepoCreateRecordAsync(
    new ComAtproto.Repo.CreateRecordInput
    {
        Repo = new ATIdentifier(client.AuthenticatedDid!),
        Collection = AppBsky.Feed.Post.RecordType,
        Record = post.ToJson(),
    });
```

Key differences:

- CarpaNet uses **parameter/input record objects** instead of method parameters
- Method names follow the NSID pattern: `AppBskyActorGetProfileAsync` = `app.bsky.actor.getProfile`
- Creating records is done via the generic `ComAtprotoRepoCreateRecordAsync` with `Record = post.ToJson()`; FishyFlip provides typed helpers like `CreatePostAsync`

## Error Handling

FishyFlip uses a `Result<T>` type (OneOf-based) with tuple deconstruction:

```csharp
// FishyFlip
var (profile, error) = await protocol.Actor.GetProfileAsync(handle);
if (error is not null)
{
    Console.WriteLine($"Error: {error.Detail?.Message}");
    return;
}
Console.WriteLine(profile!.DisplayName);
```

CarpaNet throws exceptions on failure — use standard try/catch:

```csharp
// CarpaNet
try
{
    var profile = await client.AppBskyActorGetProfileAsync(
        new AppBsky.Actor.GetProfileParameters { Actor = handle });
    Console.WriteLine(profile.DisplayName);
}
catch (ATProtoException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
```

## Authentication

### App Password

```csharp
// FishyFlip
var protocol = new ATProtocolBuilder().Build();
var (session, error) = await protocol.AuthenticateWithPasswordResultAsync(
    "alice.bsky.social", "xxxx-xxxx-xxxx-xxxx");

// CarpaNet
var client = await ATProtoClient.CreateWithSessionAsync(
    identifier: "alice.bsky.social",
    password: "xxxx-xxxx-xxxx-xxxx",
    options: new ATProtoClientOptions
    {
        JsonOptions = ATProtoJsonContext.DefaultOptions,
        CborContext = ATProtoCborContext.Default,
    });
```

### OAuth 2.0

```csharp
// FishyFlip — OAuth is on ATProtocol directly
var protocol = new ATProtocolBuilder().Build();
var (authUrl, error) = await protocol.GenerateOAuth2AuthenticationUrlResultAsync(
    clientId: "http://localhost",
    redirectUrl: "http://localhost:3000/callback",
    scopes: new[] { "atproto" },
    instanceUrl: "https://bsky.social");
// ... user completes browser login ...
var (session, error) = await protocol.AuthenticateWithOAuth2CallbackResultAsync(callbackUrl);

// CarpaNet — OAuth is a separate OAuthSession class
using var oauthSession = new OAuthSession(new OAuthClientConfig
{
    ClientId = OAuthClientConfig.CreateLoopbackClientId(8080),
    RedirectUri = OAuthClientConfig.CreateLoopbackRedirectUri(8080),
    Scope = "atproto transition:generic",
    JsonOptions = ATProtoJsonContext.DefaultOptions,
    SessionStore = new MemoryOAuthSessionStore(),
});
var authUrl = await oauthSession.AuthorizeAsync("alice.bsky.social");
// ... user completes browser login ...
ATProtoOAuthClient atClient = await oauthSession.CallbackAsync(callbackUrl);
```

### Session Persistence

```csharp
// FishyFlip — serialize AuthSession to string
var saved = await protocol.RefreshAuthSessionResultAsync();
File.WriteAllText("session.json", saved.ToString());
// Restore:
var restored = AuthSession.FromString(File.ReadAllText("session.json"));
await protocol.AuthenticateWithOAuth2SessionResultAsync(restored, clientId);

// CarpaNet — implement ISessionStore (password) or IOAuthSessionStore (OAuth)
var client = ATProtoClientFactory.Create(new ATProtoClientOptions
{
    SessionStore = new MyFileSessionStore(),
});
bool restored = await client.RestoreSessionAsync(userDid);
```

## Identity Types

Both libraries use similar strongly-typed identifiers but with different creation patterns:

```csharp
// FishyFlip
var did = ATDid.Create("did:plc:abc123");
var handle = ATHandle.Create("alice.bsky.social");
var uri = ATUri.Create("at://did:plc:abc123/app.bsky.feed.post/rkey");
var identifier = ATIdentifier.Create("alice.bsky.social");

// CarpaNet — constructors and implicit conversion
var did = new ATDid("did:plc:abc123");
var handle = new ATHandle("alice.bsky.social");
var uri = ATUri.Create("did:plc:abc123", "app.bsky.feed.post", "rkey");
var identifier = new ATIdentifier("alice.bsky.social");
```

## Jetstream

FishyFlip uses an event-based callback model:

```csharp
// FishyFlip
var jetStream = new ATJetStream(new ATJetStreamOptions
{
    Url = new Uri("wss://jetstream.atproto.tools"),
    WantedCollections = new[] { "app.bsky.feed.post" },
});
jetStream.OnRecordReceived += (s, e) => { /* handle event */ };
await jetStream.ConnectAsync();
```

CarpaNet uses `IAsyncEnumerable` for a cleaner streaming pattern:

```csharp
// CarpaNet
using var client = new JetstreamClient(
    new Uri("https://jetstream1.us-east.bsky.network"));

await foreach (var evt in client.SubscribeAsync(new JetstreamSubscribeOptions
{
    WantedCollections = new[] { "app.bsky.feed.post" },
    Compress = true,
}))
{
    if (evt.Kind == "commit" && evt.Commit is { } commit)
        Console.WriteLine($"{commit.Operation} {commit.Collection}/{commit.Rkey}");
}
```

## Serialization Context

FishyFlip manages its `SourceGenerationContext` internally — you don't need to think about it. CarpaNet requires you to pass source-generated contexts explicitly (they are generated per-project based on your declared lexicons):

```csharp
// CarpaNet — contexts are auto-generated, used via ATProtoClientFactory or manually
var client = ATProtoClientFactory.Create(); // contexts pre-wired

// Or manually:
var client = ATProtoClient.Create(new ATProtoClientOptions
{
    JsonOptions = ATProtoJsonContext.DefaultOptions,
    CborContext = ATProtoCborContext.Default,
});
```

## Quick Reference

| Operation | FishyFlip | CarpaNet |
|-----------|-----------|----------|
| Get profile | `protocol.Actor.GetProfileAsync(handle)` | `client.AppBskyActorGetProfileAsync(new ... { Actor = handle })` |
| Create post | `protocol.Feed.CreatePostAsync(post)` | `client.ComAtprotoRepoCreateRecordAsync(new ... { Record = post.ToJson() })` |
| Delete post | `protocol.Feed.DeletePostAsync(repo, rkey)` | `client.ComAtprotoRepoDeleteRecordAsync(new ... { Rkey = rkey })` |
| Get timeline | `protocol.Feed.GetTimelineAsync(limit)` | `client.AppBskyFeedGetTimelineAsync(new ... { Limit = limit })` |
| Resolve handle | `protocol.ResolveATIdentifierAsync(handle)` | `resolver.ResolveAsync("alice.bsky.social")` |
| Check auth | `protocol.IsAuthenticated` | `client.IsAuthenticated` |
| Get current DID | `protocol.Session?.Did` | `client.AuthenticatedDid` |

## Summary of Key Differences (It's a numbered list so you know an LLM did this)

1. **Lexicon-driven**: CarpaNet generates only the APIs you declare — add lexicons to `.csproj`
2. **No endpoint groups**: Flat extension methods named after NSIDs instead of `protocol.Actor.*`
3. **No Result type**: CarpaNet throws exceptions instead of returning `Result<T>`
4. **Explicit serialization contexts**: You pass `JsonOptions`/`CborContext` (auto-generated per project)
5. **Modular packages**: OAuth and Jetstream are separate NuGet packages
6. **IAsyncEnumerable streams**: Jetstream and firehose use `await foreach` instead of event callbacks
7. **Constructor-based types**: `new ATHandle(...)` instead of `ATHandle.Create(...)`
8. **Generic record operations**: Use `ComAtprotoRepoCreateRecordAsync` with `Record = obj.ToJson()` instead of typed `CreatePostAsync` helpers
