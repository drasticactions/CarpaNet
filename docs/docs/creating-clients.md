# Creating Clients

## Public (Unauthenticated) Client

Uses the Bluesky public AppView — can only make GET requests:

```csharp
using CarpaNet;

// ATProtoClientFactory is source-generated with your JSON/CBOR contexts preconfigured
var client = ATProtoClientFactory.Create();

var profile = await client.AppBskyActorGetProfileAsync(
    new AppBsky.Actor.GetProfileParameters { Actor = new ATHandle("alice.bsky.social") });

Console.WriteLine($"{profile.DisplayName} (@{profile.Handle})");
```

## Authenticated Client (App Password)

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

## Authenticated Client with Custom Options

```csharp
var client = ATProtoClientFactory.Create(new ATProtoClientOptions
{
    SessionStore = new MySessionStore(),       // persist sessions across restarts
    EnableRateLimitHandler = true,             // automatic 429 retry (default: true)
    AutoRetryOnAuthFailure = true,             // retry on 401 with token refresh (default: true)
    RateLimitMaxRetries = 3,
    UserAgent = "MyApp/1.0",
    LoggerFactory = loggerFactory,
});
```

## Restoring a Session

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

## Listening for Token Refreshes

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
