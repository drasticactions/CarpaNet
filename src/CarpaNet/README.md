# CarpaNet

[![NuGet Version](https://img.shields.io/nuget/v/CarpaNet.svg)](https://www.nuget.org/packages/CarpaNet/) ![License](https://img.shields.io/badge/License-MIT-blue.svg)

![CarpaNet Logo](https://user-images.githubusercontent.com/898335/253740405-4b0ae177-cc49-4c26-b6b0-ab8e835a0e62.png)

CarpaNet is the core .NET runtime library for interacting with [ATProtocol](https://atproto.com). It provides ATProtocol primitives, HTTP clients, OAuth support (Via CarpaNet.OAuth), identity resolution, CBOR serialization, event streams, Jetstream support (Via CarpaNet.Jetstream) and repo reading. 

## Installation

```
dotnet add package CarpaNet
```

By itself, CarpaNet does **not** provide bindings to any ATProtocol lexicons. You can either write them yourself or use `CarpaNet.SourceGen` to create bound objects.

## IATProtoClient

All clients implement `IATProtoClient`, which provides `GetAsync`, `PostAsync`, and `SubscribeAsync` methods for XRPC calls. The source generator produces typed extension methods on `IATProtoClient` (e.g., `client.AppBskyFeedGetTimelineAsync()`), so any client works with the generated API surface. You can either write your own implementation, or use the ones provided by `CarpaNet` or `CarpaNet.OAuth`.

### ATProtoClient

The provided implementation client for CarpaNet. Supports public (unauthenticated) access, session-based authentication (App Passwords), and custom token providers. Includes auto-retry on auth failure and rate limiting.

```csharp
// Public (unauthenticated) access
var client = ATProtoClient.Create(new ATProtoClientOptions
{
    JsonOptions = myJsonOptions,
    CborContext = myCborContext
});

var session = await client.LoginAsync("myhandle.bsky.social", "my-app-password");

// Or authenticate via password session directly using static factory.
var client = await ATProtoClient.CreateWithSessionAsync(
    "myhandle.bsky.social", "my-app-password",
    options: new ATProtoClientOptions
    {
        JsonOptions = myJsonOptions,
        CborContext = myCborContext
    });
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
- `DPoPTokenProvider` — For OAuth sessions with DPoP proof-of-possession, you can find this in CarpaNet.OAuth.

Both support automatic token refresh and raise `TokenRefreshed` events for session persistence.

## Storage Interfaces

OAuth flows require persistent storage. CarpaNet provides interfaces and in-memory defaults:

- `IOAuthSessionStore` — Store/retrieve/delete OAuth session data by DID
- `IOAuthStateStore` — Store/consume OAuth authorization state (replay-safe)
- `MemoryOAuthSessionStore` / `MemoryOAuthStateStore` — In-memory implementations
