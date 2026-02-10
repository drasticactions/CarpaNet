# CarpaNet

[![NuGet Version](https://img.shields.io/nuget/v/CarpaNet.svg)](https://www.nuget.org/packages/CarpaNet/) ![License](https://img.shields.io/badge/License-MIT-blue.svg)

![CarpaNet Logo](https://user-images.githubusercontent.com/898335/253740405-4b0ae177-cc49-4c26-b6b0-ab8e835a0e62.png)

CarpaNet is the core .NET runtime library for interacting with [ATProtocol](https://atproto.com). It provides ATProtocol primitives, HTTP clients, OAuth support, identity resolution, CBOR serialization, event streams, and repo reading. 

## Installation

```
dotnet add package CarpaNet
```

## Clients

All clients implement `IATProtoClient`, which provides `GetAsync`, `PostAsync`, and `SubscribeAsync` methods for XRPC calls. The source generator produces typed extension methods on `IATProtoClient` (e.g., `client.AppBskyFeedGetTimelineAsync()`), so any client works with the generated API surface.

### ATProtoClient

Full-featured client with pluggable authentication via `ITokenProvider`. Supports auto-retry on auth failure and rate limiting.

```csharp
// Public (unauthenticated) access
var client = new ATProtoClient(new ATProtoClientOptions
{
    BaseUrl = new Uri("https://public.api.bsky.app"),
    JsonOptions = myJsonOptions,
    CborContext = myCborContext
});

// Authenticated via password session (using static factory)
var client = await ATProtoClient.CreateWithSessionAsync(
    "myhandle.bsky.social", "my-app-password",
    options: new ATProtoClientOptions
    {
        JsonOptions = myJsonOptions,
        CborContext = myCborContext
    });
```

### ATProtoSessionClient

Session-based (app password) client with `LoginAsync`, `LogoutAsync`, and `RestoreSession`.

```csharp
var client = new ATProtoSessionClient(httpClient, jsonOptions, cborContext);
var session = await client.LoginAsync("myhandle.bsky.social", "my-app-password");
```

### ATProtoPublicClient

Unauthenticated client for public read-only API access. POST methods throw `NotSupportedException`.

```csharp
var client = new ATProtoPublicClient(httpClient, jsonOptions, cborContext);
```

### ATProtoOAuthClient

OAuth 2.0 flow orchestrator supporting PAR (Pushed Authorization Requests), PKCE, and DPoP. This class is not an `IATProtoClient` itself — it produces an authenticated `ATProtoClient` via `CallbackAsync` or `RestoreSessionAsync`.

```csharp
var config = new OAuthClientConfig
{
    ClientId = clientId,
    RedirectUri = redirectUri,
    Scope = "atproto transition:generic",
    JsonOptions = myJsonOptions,
    SessionStore = mySessionStore
};

using var oauthClient = new ATProtoOAuthClient(config);
var authUrl = await oauthClient.AuthorizeAsync(handle);
// ... redirect user to authUrl, receive callback ...
var session = await oauthClient.CallbackAsync(callbackUrl);
// session implements IATProtoClient
```

## Configuration

`ATProtoClientOptions` controls client behavior:

| Property | Description | Default |
|---|---|---|
| `BaseUrl` | Base URL for API requests | — |
| `JsonOptions` | `JsonSerializerOptions` with source-gen resolver (required for AOT) | — |
| `CborContext` | `CborSerializerContext` for event stream deserialization (required for AOT) | — |
| `TokenProvider` | `ITokenProvider` for auth; null = public mode | `null` |
| `IdentityResolver` | Handle/DID resolver | Auto-created |
| `AutoRetryOnAuthFailure` | Retry on 401 with token refresh | `true` |
| `EnableRateLimitHandler` | Rate limit handling | `true` |
| `AutoRetryOnRateLimit` | Auto-retry on rate limit | `true` |
| `RateLimitMaxRetries` | Max rate limit retries | `3` |
| `UserAgent` | User-Agent header | `null` |
| `Timeout` | Request timeout | `100s` |
| `LabelerDids` | Labeler DIDs for `atproto-accept-labelers` header | `null` |

## ATProtocol Types

- `ATDid` — Strongly-typed DID (e.g., `did:plc:...`)
- `ATHandle` — Strongly-typed handle (e.g., `myname.bsky.social`)
- `ATUri` — AT URI with authority, collection, and record key parsing
- `ATCid` — Content identifier (CID)
- `ATIdentifier` — Accepts either a DID or handle
- `BlobRef` — Blob reference with CID link

## Identity Resolution

`IdentityResolver` resolves handles to DIDs and DIDs to DID documents. Supports both `did:plc` (via PLC directory) and `did:web`. Handle resolution uses DNS TXT records with HTTPS fallback.

```csharp
var resolver = IdentityResolver.CreateWithCache();
var didDoc = await resolver.ResolveAsync("myhandle.bsky.social");
var pdsEndpoint = didDoc.PdsEndpoint;
```

## CBOR Serialization

The `CarpaNet.Cbor` namespace provides DAG-CBOR serialization using `System.Formats.Cbor`. The `CborSerializerContext` pattern mirrors `System.Text.Json`'s `JsonSerializerContext` for AOT compatibility. Used for event stream message deserialization and repository operations.

## Event Streams

`EventStreamClient` provides WebSocket-based subscription to ATProtocol event streams, with CBOR frame parsing. This is used internally by the clients' `SubscribeAsync` method.

## Repository

The `Repo` namespace includes `CarReader` for reading CAR (Content Addressable aRchive) files, `Repository` for working with ATProtocol repositories, and `MstNode`/`RepoCommit` for Merkle Search Tree operations.

## Authentication

`ITokenProvider` abstracts token management. Two implementations are provided:

- `SessionTokenProvider` — For app password sessions (JWT access/refresh tokens)
- `DPoPTokenProvider` — For OAuth sessions with DPoP proof-of-possession

Both support automatic token refresh and raise `TokenRefreshed` events for session persistence.

## Storage Interfaces

OAuth flows require persistent storage. CarpaNet provides interfaces and in-memory defaults:

- `IOAuthSessionStore` — Store/retrieve/delete OAuth session data by DID
- `IOAuthStateStore` — Store/consume OAuth authorization state (replay-safe)
- `MemoryOAuthSessionStore` / `MemoryOAuthStateStore` — In-memory implementations
