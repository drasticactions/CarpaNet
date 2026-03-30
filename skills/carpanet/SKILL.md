---
name: carpanet
description: >
  Guide for building .NET applications with CarpaNet, an ATProtocol library with Roslyn source generators.
  Use when: (1) Setting up a project with CarpaNet source generation from Lexicon JSON files,
  (2) Creating authenticated or public ATProtoClient instances, (3) Performing XRPC queries and procedures
  (get profiles, create/delete posts, list records), (4) Implementing OAuth 2.0 with DPoP for user-facing apps,
  (5) Subscribing to real-time events via Jetstream or the ATProtocol firehose, (6) Resolving handles and DIDs
  with IdentityResolver, (7) Reading ATProtocol repositories and CAR files, (8) Configuring MSBuild properties
  for lexicon resolution, JSON/CBOR context generation, and NativeAOT publishing.
---

# CarpaNet Usage Guide

This skill helps you use CarpaNet to build .NET applications that interact with ATProtocol. CarpaNet uses Roslyn source generators to produce type-safe API bindings from Lexicon JSON files, with full NativeAOT compatibility.

## Prerequisites

- .NET 8 or above SDK
- `dotnet add package CarpaNet --prerelease`

## Project Setup

### Minimal .csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CarpaNet" />
  </ItemGroup>

  <!-- Declare which lexicons you need -->
  <ItemGroup>
    <LexiconResolve Include="app.bsky.actor.getProfile" />
    <LexiconResolve Include="app.bsky.feed.post" />
    <LexiconResolve Include="com.atproto.repo.createRecord" />
    <LexiconResolve Include="com.atproto.repo.deleteRecord" />
  </ItemGroup>
</Project>
```

When you build, the source generator resolves lexicons via DNS, caches them locally, and generates:
- Data model classes with JSON attributes
- Extension methods on `IATProtoClient` for each query/procedure
- A `JsonSerializerContext` and `CborSerializerContext` for AOT-compatible serialization
- `ToJson()`/`FromJson()` helpers on each generated type
- An `ATProtoClientFactory` with preconfigured JSON/CBOR contexts

### Lexicon Sources

You can combine four ways to supply lexicons:

```xml
<ItemGroup>
  <!-- 1. Local JSON files -->
  <LexiconFiles Include="lexicons/**/*.json" />

  <!-- 2. Resolve specific NSIDs via DNS -->
  <LexiconResolve Include="app.bsky.feed.post" />

  <!-- 3. Fetch all lexicons published by an authority -->
  <LexiconResolveAuthority Include="blog.pckt" />
  <LexiconResolveAuthority Include="site.standard" />

  <!-- 4. Fetch all lexicons published by a handle -->
  <LexiconResolveHandle Include="atproto-lexicons.bsky.social" />
</ItemGroup>
```

### Auto-Resolve Transitive Dependencies

Enable automatic discovery of referenced lexicons you haven't explicitly listed:

```xml
<PropertyGroup>
  <CarpaNet_LexiconAutoResolve>true</CarpaNet_LexiconAutoResolve>
</PropertyGroup>
```

This scans your lexicons for `ref` fields pointing to external NSIDs, resolves them via DNS, and repeats until all dependencies are satisfied.

### MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| `CarpaNet_JsonContextName` | `ATProtoJsonContext` | Name of the generated JSON serializer context |
| `CarpaNet_CborContextName` | `ATProtoCborContext` | Name of the generated CBOR serializer context |
| `CarpaNet_SourceGen_RootNamespace` | Project namespace | Root namespace for generated code |
| `CarpaNet_SourceGen_EmitValidationAttributes` | `false` | Emit `[ATStringLength]`, `[Range]` attributes |
| `CarpaNet_LexiconAutoResolve` | `false` | Auto-resolve transitive lexicon dependencies |
| `CarpaNet_LexiconAutoResolveMaxDepth` | `10` | Max iterations for transitive resolution |
| `CarpaNet_LexiconCacheDir` | `obj/lexicon-cache/` | Cache directory for resolved lexicons |
| `CarpaNet_LexiconCacheTtlHours` | `24` | Cache TTL in hours; `0` forces refresh |
| `CarpaNet_LexiconFailOnError` | `true` | Fail build on resolution errors |
| `CarpaNet_PlcDirectoryUrl` | `https://plc.directory` | PLC directory URL |
| `CarpaNet_DnsServers` | (empty) | Semicolon-separated DNS server IPs |

### Inspecting Generated Code

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Generated files appear at `obj/Debug/{TFM}/generated/CarpaNet.SourceGen/CarpaNet.LexiconGenerator/`.

---

## Creating Clients

### Public (Unauthenticated) Client

Uses the Bluesky public AppView — can only make GET requests:

```csharp
using CarpaNet;

// ATProtoClientFactory is source-generated with your JSON/CBOR contexts preconfigured
var client = ATProtoClientFactory.Create();

var profile = await client.AppBskyActorGetProfileAsync(
    new AppBsky.Actor.GetProfileParameters { Actor = new ATHandle("alice.bsky.social") });

Console.WriteLine($"{profile.DisplayName} (@{profile.Handle})");
```

### Authenticated Client (App Password)

```csharp
var client = await ATProtoClient.CreateWithSessionAsync(
    identifier: "alice.bsky.social",    // handle, email, or DID
    password: "xxxx-xxxx-xxxx-xxxx",    // app password
    options: new ATProtoClientOptions
    {
        JsonOptions = ATProtoJsonContext.DefaultOptions,
        CborContext = ATProtoCborContext.Default,
    });

// Now you can make POST requests
var timeline = await client.AppBskyFeedGetTimelineAsync(
    new AppBsky.Feed.GetTimelineParameters { Limit = 10 });
```

### Authenticated Client with Custom Options

```csharp
var client = ATProtoClient.Create(new ATProtoClientOptions
{
    JsonOptions = ATProtoJsonContext.DefaultOptions,
    CborContext = ATProtoCborContext.Default,
    SessionStore = new MySessionStore(),       // persist sessions across restarts
    EnableRateLimitHandler = true,             // automatic 429 retry (default: true)
    AutoRetryOnAuthFailure = true,             // retry on 401 with token refresh (default: true)
    RateLimitMaxRetries = 3,
    UserAgent = "MyApp/1.0",
    LoggerFactory = loggerFactory,
});
```

### Restoring a Session

```csharp
// From explicit tokens
var client = ATProtoClient.CreateWithRestoredSession(
    accessJwt: savedAccessJwt,
    refreshJwt: savedRefreshJwt,
    did: savedDid,
    handle: savedHandle,
    pdsUrl: new Uri(savedPdsUrl));

// Or from a session store
var client = ATProtoClientFactory.Create(new ATProtoClientOptions
{
    SessionStore = new MySessionStore(),
});
bool restored = await client.RestoreSessionAsync(userDid);
```

### Listening for Token Refreshes

```csharp
if (client.TokenProvider is { } provider)
{
    provider.TokenRefreshed += (sender, args) =>
    {
        // Persist new tokens
        SaveTokens(args.Did, args.AccessToken, args.RefreshToken);
    };
}
```

---

## Common Operations

### Get a Profile

```csharp
var profile = await client.AppBskyActorGetProfileAsync(
    new AppBsky.Actor.GetProfileParameters { Actor = new ATHandle("alice.bsky.social") });
```

### Create a Post

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

### Delete a Record

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

### List Records

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

### Get a Timeline

```csharp
var timeline = await client.AppBskyFeedGetTimelineAsync(
    new AppBsky.Feed.GetTimelineParameters { Limit = 25 });

foreach (var item in timeline.Feed)
{
    Console.WriteLine($"{item.Post.Author.Handle}: {item.Post.Record}");
}
```

---

## OAuth Authentication

OAuth is required for some operations and is the recommended auth method for user-facing apps. Requires the `CarpaNet.OAuth` package.

### Desktop/Console App Flow

```csharp
using CarpaNet.OAuth;

// 1. Configure with loopback URI for desktop apps
var port = 8080;
var config = new OAuthClientConfig
{
    ClientId = OAuthClientConfig.CreateLoopbackClientId(port),
    RedirectUri = OAuthClientConfig.CreateLoopbackRedirectUri(port),
    Scope = "atproto transition:generic",
    JsonOptions = ATProtoJsonContext.DefaultOptions,
    SessionStore = new MemoryOAuthSessionStore(),
};

// 2. Start the OAuth flow
using var oauthSession = new OAuthSession(config);
var authUrl = await oauthSession.AuthorizeAsync("alice.bsky.social");

// 3. Open browser and listen for callback
Console.WriteLine($"Open: {authUrl}");
// ... start HTTP listener on port, capture callback URL ...

// 4. Exchange code for tokens
ATProtoOAuthClient atClient = await oauthSession.CallbackAsync(callbackUrl);

// 5. Use the authenticated client
var profile = await atClient.AppBskyActorGetProfileAsync(
    new AppBsky.Actor.GetProfileParameters { Actor = new ATHandle("alice.bsky.social") });

// 6. Sign out when done
await atClient.SignOutAsync();
```

### Web App Flow

```csharp
var config = new OAuthClientConfig
{
    ClientId = "https://myapp.example.com/client-metadata.json",
    RedirectUri = "https://myapp.example.com/callback",
    Scope = "atproto transition:generic",
    JsonOptions = ATProtoJsonContext.DefaultOptions,
    SessionStore = myPersistentSessionStore,
    StateStore = myPersistentStateStore,
};

using var oauthSession = new OAuthSession(config);
var authUrl = await oauthSession.AuthorizeAsync(userHandle);
// Redirect user to authUrl...

// In callback handler:
var atClient = await oauthSession.CallbackAsync(Request.Url.ToString());
```

### Restoring an OAuth Session

```csharp
var config = new OAuthClientConfig
{
    ClientId = savedClientId,
    RedirectUri = savedRedirectUri,
    Scope = "atproto",
    JsonOptions = ATProtoJsonContext.DefaultOptions,
    SessionStore = mySessionStore,
};

using var oauthSession = new OAuthSession(config);
ATProtoOAuthClient atClient = await oauthSession.RestoreSessionAsync(userDid);
```

### Custom Session Storage

Implement `IOAuthSessionStore` to persist OAuth sessions (DPoP keys, tokens) across app restarts:

```csharp
public sealed class FileOAuthSessionStore : IOAuthSessionStore
{
    private readonly string _directory;

    public FileOAuthSessionStore(string directory) => _directory = directory;

    public Task StoreAsync(string sub, OAuthSessionData data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data);
        File.WriteAllText(GetPath(sub), json);
        return Task.CompletedTask;
    }

    public Task<OAuthSessionData?> GetAsync(string sub, CancellationToken ct)
    {
        var path = GetPath(sub);
        if (!File.Exists(path)) return Task.FromResult<OAuthSessionData?>(null);
        var data = JsonSerializer.Deserialize<OAuthSessionData>(File.ReadAllText(path));
        return Task.FromResult(data);
    }

    public Task DeleteAsync(string sub, CancellationToken ct)
    {
        File.Delete(GetPath(sub));
        return Task.CompletedTask;
    }

    private string GetPath(string sub) =>
        Path.Combine(_directory, $"oauth-{sub.Replace(":", "_")}.json");
}
```

---

## Jetstream (Real-Time Events)

Jetstream provides a lightweight, JSON-based WebSocket event stream. Requires the `CarpaNet.Jetstream` package.

```csharp
using CarpaNet.Jetstream;

using var client = new JetstreamClient(
    new Uri("https://jetstream1.us-east.bsky.network"));

var options = new JetstreamSubscribeOptions
{
    WantedCollections = new[] { "app.bsky.feed.post", "app.bsky.feed.like" },
    WantedDids = new[] { "did:plc:z72i7hdynmk6r22z27h6tvur" },  // optional, max 10,000
    Cursor = 1725911162329308,   // optional, resume from Unix microsecond timestamp
    Compress = true,             // enable zstd compression
};

await foreach (var evt in client.SubscribeAsync(options))
{
    switch (evt.Kind)
    {
        case "commit" when evt.Commit is { } commit:
            Console.WriteLine($"[{commit.Operation}] {commit.Collection}/{commit.Rkey}");
            if (commit.Record is { } record)
            {
                // record is a JsonElement — parse with your generated types
                var type = record.TryGetProperty("$type", out var t) ? t.GetString() : null;
                Console.WriteLine($"  $type={type}");
            }
            break;

        case "identity" when evt.Identity is { } identity:
            Console.WriteLine($"[Identity] {evt.Did} → {identity.Handle}");
            break;

        case "account" when evt.Account is { } account:
            Console.WriteLine($"[Account] {evt.Did} active={account.Active} status={account.Status}");
            break;
    }
}
```

### Dynamic Filter Updates

```csharp
await client.SendOptionsUpdateAsync(new JetstreamOptionsUpdate
{
    WantedCollections = new[] { "app.bsky.graph.follow" },
});
```

---

## Firehose (Full Event Stream)

Subscribe to the ATProtocol firehose for CBOR-encoded, batched commit events. Uses the core CarpaNet package — add `com.atproto.sync.subscribeRepos` to your lexicon resolves.

```csharp
var client = ATProtoClientFactory.Create(new ATProtoClientOptions
{
    BaseUrl = new Uri("https://bsky.network"),  // relay URL
});

await foreach (var message in client.ComAtprotoSyncSubscribeReposAsync(cancellationToken: cts.Token))
{
    switch (message)
    {
        case ComAtproto.Sync.SubscribeReposCommit commit:
            Console.WriteLine($"[Commit] seq={commit.Seq} repo={commit.Repo}");
            foreach (var op in commit.Ops ?? [])
            {
                Console.WriteLine($"  {op.Action} {op.Path}");
            }
            break;

        case ComAtproto.Sync.SubscribeReposIdentity identity:
            Console.WriteLine($"[Identity] {identity.Did} → {identity.Handle}");
            break;

        case ComAtproto.Sync.SubscribeReposAccount account:
            Console.WriteLine($"[Account] {account.Did} active={account.Active}");
            break;
    }
}
```

---

## Identity Resolution

Resolve handles to DIDs and DID documents:

```csharp
using CarpaNet.Identity;

// Create with in-memory caching
var resolver = IdentityResolver.CreateWithCache();

// Handle → DID document
var didDoc = await resolver.ResolveAsync("alice.bsky.social");
Console.WriteLine($"DID: {didDoc.Id}");
Console.WriteLine($"PDS: {didDoc.PdsEndpoint}");
Console.WriteLine($"Handle: {didDoc.Handle}");

// DID → DID document
var didDoc2 = await resolver.ResolveAsync("did:plc:z72i7hdynmk6r22z27h6tvur");
```

The `ATProtoClient` creates an `IdentityResolver` automatically (configurable via `ATProtoClientOptions.CreateIdentityResolver`).

---

## Repository & CAR File Reading

Read ATProtocol repositories from CAR (Content Addressable aRchive) files:

```csharp
using CarpaNet.Repo;

// Load from file
var repo = Repository.LoadFromFile("repository.car");

// Or from stream/bytes
var repo = Repository.Load(carStream);
var repo = Repository.Load(carBytes);

// Inspect
Console.WriteLine($"Owner: {repo.Did}");
Console.WriteLine($"Revision: {repo.Rev}");
Console.WriteLine($"Root CID: {repo.RootCid}");

// Low-level CAR block reading
using var reader = new CarReader(stream);
foreach (var block in reader.ReadBlocks())
{
    Console.WriteLine($"CID: {block.Cid}, Size: {block.Data.Length}");
}
```

---

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
Console.WriteLine(cid.IsAtProtoBlessedFormat);  // true
```

All identifier types support equality, implicit string conversion, and JSON serialization.

---

## Working with Custom Lexicons

You can use your own or third-party lexicons beyond Bluesky's. Place JSON files in your project and reference them:

```xml
<ItemGroup>
  <LexiconFiles Include="lexicons/**/*.json" />

  <!-- Or resolve third-party lexicons by authority -->
  <LexiconResolveAuthority Include="blog.pckt" />
  <LexiconResolveAuthority Include="site.standard" />
</ItemGroup>
```

The source generator produces the same type-safe bindings for custom lexicons. Use the generated `FromJson()` method to parse records:

```csharp
var records = await client.ComAtprotoRepoListRecordsAsync(
    new ComAtproto.Repo.ListRecordsParameters
    {
        Repo = "did:plc:example",
        Collection = MyCustom.Namespace.MyRecord.RecordType,
    });

foreach (var record in records.Records)
{
    var parsed = MyCustom.Namespace.MyRecord.FromJson(record.Value);
    Console.WriteLine(parsed?.SomeField);
}
```

---

## NativeAOT Publishing

CarpaNet is fully NativeAOT compatible. The source-generated JSON and CBOR contexts eliminate reflection:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

```bash
dotnet publish -c Release
```

No additional configuration needed — the generated `ATProtoJsonContext` and `ATProtoCborContext` handle all serialization at compile time.

---

## Migrating from FishyFlip

This section helps users transition from [FishyFlip](https://github.com/drasticactions/FishyFlip) to CarpaNet. Both libraries target ATProtocol but differ significantly in architecture and API design.

### Package Mapping

| FishyFlip | CarpaNet | Notes |
|-----------|----------|-------|
| `FishyFlip` | `CarpaNet` | Core library |
| `FishyFlip` (built-in) | `CarpaNet.Jetstream` | Jetstream is a separate package in CarpaNet |
| `FishyFlip` (built-in) | `CarpaNet.OAuth` | OAuth is a separate package in CarpaNet |
| `FishyFlip.AspNetCore` | *(not yet available)* | ASP.NET Core integration |

### Lexicon Declaration (New Concept)

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

To pull in all lexicons from Bluesky at once, use authority resolution:

```xml
<ItemGroup>
  <LexiconResolveHandle Include="atproto-lexicons.bsky.social" />
</ItemGroup>
```

### Client Creation

**FishyFlip** uses a builder pattern that creates an `ATProtocol` instance:

```csharp
// FishyFlip
var protocol = new ATProtocolBuilder()
    .WithInstanceUrl(new Uri("https://bsky.social"))
    .WithUserAgent("MyApp/1.0")
    .WithLogger(logger)
    .EnableAutoRenewSession(true)
    .Build();
```

**CarpaNet** uses static factory methods or constructor with options. A source-generated `ATProtoClientFactory` provides pre-configured JSON/CBOR contexts:

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

### API Call Patterns

**FishyFlip** organizes methods into endpoint groups (`protocol.Actor`, `protocol.Feed`, etc.):

```csharp
// FishyFlip — endpoint groups
var (profile, error) = await protocol.Actor.GetProfileAsync(
    ATHandle.Create("alice.bsky.social"));

var (timeline, error) = await protocol.Feed.GetTimelineAsync(limit: 25);

var (result, error) = await protocol.Feed.CreatePostAsync(post);
```

**CarpaNet** uses flat extension methods on `IATProtoClient`, named after the NSID:

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

### Error Handling

**FishyFlip** uses a `Result<T>` type (OneOf-based) with tuple deconstruction:

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

**CarpaNet** throws exceptions on failure — use standard try/catch:

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

### Authentication

#### App Password

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

#### OAuth 2.0

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

#### Session Persistence

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

### Identity Types

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

### Jetstream (Real-Time Events)

**FishyFlip** uses an event-based callback model:

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

**CarpaNet** uses `IAsyncEnumerable` for a cleaner streaming pattern:

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

### Serialization Context

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

### Quick Reference: Common Operations

| Operation | FishyFlip | CarpaNet |
|-----------|-----------|----------|
| Get profile | `protocol.Actor.GetProfileAsync(handle)` | `client.AppBskyActorGetProfileAsync(new ... { Actor = handle })` |
| Create post | `protocol.Feed.CreatePostAsync(post)` | `client.ComAtprotoRepoCreateRecordAsync(new ... { Record = post.ToJson() })` |
| Delete post | `protocol.Feed.DeletePostAsync(repo, rkey)` | `client.ComAtprotoRepoDeleteRecordAsync(new ... { Rkey = rkey })` |
| Get timeline | `protocol.Feed.GetTimelineAsync(limit)` | `client.AppBskyFeedGetTimelineAsync(new ... { Limit = limit })` |
| Resolve handle | `protocol.ResolveATIdentifierAsync(handle)` | `resolver.ResolveAsync("alice.bsky.social")` |
| Check auth | `protocol.IsAuthenticated` | `client.IsAuthenticated` |
| Get current DID | `protocol.Session?.Did` | `client.AuthenticatedDid` |

### Summary of Key Differences

1. **Lexicon-driven**: CarpaNet generates only the APIs you declare — add lexicons to `.csproj`
2. **No endpoint groups**: Flat extension methods named after NSIDs instead of `protocol.Actor.*`
3. **No Result type**: CarpaNet throws exceptions instead of returning `Result<T>`
4. **Explicit serialization contexts**: You pass `JsonOptions`/`CborContext` (auto-generated per project)
5. **Modular packages**: OAuth and Jetstream are separate NuGet packages
6. **IAsyncEnumerable streams**: Jetstream and firehose use `await foreach` instead of event callbacks
7. **Constructor-based types**: `new ATHandle(...)` instead of `ATHandle.Create(...)`
8. **Generic record operations**: Use `ComAtprotoRepoCreateRecordAsync` with `Record = obj.ToJson()` instead of typed `CreatePostAsync` helpers
