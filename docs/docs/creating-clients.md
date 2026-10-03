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

When CarpaNet creates the `HttpClient` itself, the rate-limit, timeout and `UserAgent` options are
applied to it. When you pass your own `HttpClient`, compose its handlers yourself (for example with
`HttpClientFactory.Create(new HttpClientFactoryOptions { ... })`); `UserAgent` is then added to
each request unless the `HttpClient` already sets one.

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

## Per-Request Options: Proxies, Labelers, Headers and Other Services

`WithRequestOptions` (and the shortcuts below) return a `ScopedATProtoClient` that shares the
session and HTTP pipeline of the client it wraps. Every call made through it, including the
generated API methods, uses the options.

```csharp
// Send app.bsky calls to the Bluesky AppView through the PDS.
var appview = client.WithProxy("did:web:api.bsky.app#bsky_appview");

// Let the PDS answer itself (no atproto-proxy header), e.g. for preferences.
var pds = client.WithoutProxy();

// Extra headers for one kind of call.
var feeds = appview.WithHeader("Accept-Language", "en,de");

// Accepted labelers for this scope only.
var scoped = appview.WithAcceptLabelers(new[] { AcceptLabelersHeader.Redact(modDid), myLabelerDid });

// A different service. Session credentials are never sent there; add any token yourself.
var video = client
    .WithServiceUrl(new Uri("https://video.bsky.app"))
    .WithHeader("Authorization", $"Bearer {serviceAuthToken}");
```

Options set on an outer scope win over inner scopes and over the proxy a generated method would use
(the `chat.bsky.*` methods proxy to the chat service by default).

To change the accepted labelers for every request on a client, call `SetLabelerDids`:

```csharp
client.SetLabelerDids(new[] { AcceptLabelersHeader.Redact(modDid), subscribedLabelerDid });
```

### Where credentials are sent

The access token (or DPoP proof) is attached only to requests that go to the session's own PDS.
A query with a `repo` parameter for another account is sent to that account's PDS without
credentials, and so is anything sent with `WithServiceUrl`.

### Binary bodies and responses

Procedures with a non-JSON body (`com.atproto.repo.uploadBlob`, `app.bsky.video.uploadPart`) and
queries with a non-JSON response (`com.atproto.sync.getBlob`) are generated to take a `Stream` and
return `byte[]`. They can also be called directly:

```csharp
var output = await client.PostBinaryAsync<MyOutput>(nsid, proxyServiceDid: null, parameters, stream, "video/mp4");
byte[] car = await client.GetBytesAsync("com.atproto.sync.getRepo", null, parameters);
```

A body from a seekable stream is replayed after a token refresh; a non-seekable body is sent once.
Wrap a stream in `ProgressReportingStream` to report upload progress.
