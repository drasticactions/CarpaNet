# CarpaNet.SourceGen

[![NuGet Version](https://img.shields.io/nuget/v/CarpaNet.SourceGen.svg)](https://www.nuget.org/packages/CarpaNet.SourceGen/) ![License](https://img.shields.io/badge/License-MIT-blue.svg)

![CarpaNet Logo](https://user-images.githubusercontent.com/898335/253740405-4b0ae177-cc49-4c26-b6b0-ab8e835a0e62.png)

CarpaNet.SourceGen is a Roslyn incremental source generator that reads [ATProtocol Lexicon](https://atproto.com/specs/lexicon) JSON files at compile time and generates C# code for data models, API methods, JSON serialization contexts, and CBOR serialization contexts.

![1444070256569233](https://user-images.githubusercontent.com/898335/167266846-1ad2648f-91c1-4a04-a18d-6dd4d6c7d21c.gif)

This library is experimental and not stable. Expect issues and bugs!

## Installation

```
dotnet add package CarpaNet.SourceGen
```

## Setup

### 1. Reference both packages

In order to use this, tour project needs a reference to `CarpaNet` and the `CarpaNet.SourceGen`:

```xml
<ItemGroup>
  <PackageReference Include="CarpaNet" />
  <PackageReference Include="CarpaNet.SourceGen"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

CarpaNet.SourceGen generates types that include CarpaNet, so it will complain if you don't have it.


### 2. Import the MSBuild targets (If using from source)

```xml
<Import Project="path/to/CarpaNet.SourceGen.targets" />
```

When using the NuGet package, the targets are imported automatically.

### 3. Add Lexicon files

Point `LexiconFiles` to your ATProtocol Lexicon JSON files:

```xml
<ItemGroup>
  <LexiconFiles Include="path/to/lexicons/**/*.json" />
</ItemGroup>
```

This will generate the types based on the lexicon files. You can include multiple entries, the source gen will consolidate them down, although you should try and limit the amount you generate to what your program or library needs.

### 4. Resolve Lexicons from DNS

You can also resolve lexicon schemas directly from the network using [ATProtocol's DNS-based lexicon resolution](https://atproto.com/specs/lexicon#lexicon-publication-and-resolution). Add `LexiconResolve` items with the NSIDs you want:

```xml
<ItemGroup>
  <!-- Local lexicon files -->
  <LexiconFiles Include="lexicons/**/*.json" />

  <!-- DNS-resolved lexicons -->
  <LexiconResolve Include="com.example.myapp.getProfile" />
  <LexiconResolve Include="com.example.myapp.createPost" />
</ItemGroup>
```

At build time, each NSID is resolved via DNS TXT lookup to find the publishing authority's DID, then the lexicon schema is fetched from their PDS. Resolved files are cached locally and automatically fed into `LexiconFiles`, so the source generator handles them identically to local files.

**NOTE**: Remote resolution has its pros and cons - For one, you need a network connection for it to work, and the lexicon has to be registered for it to work, which it may not be. It will fetch new versions once the TTL expires, and that can break your build if it changes. Of course, if the lexicon owner did change their spec, you may want it to break since they now changed their API.

#### How resolution works

Read the spec linked above, but the TL;DR is

1. NSID `com.example.myapp.getProfile` → authority `com.example.myapp` → DNS lookup `_lexicon.myapp.example.com`
2. DNS TXT record returns `did=did:plc:xxx`
3. DID document is resolved to find the PDS endpoint
4. Lexicon record is fetched: `GET {pds}/xrpc/com.atproto.repo.getRecord?repo={did}&collection=com.atproto.lexicon.schema&rkey={nsid}`

NSIDs sharing the same authority are grouped so the DNS + DID resolution only happens once per authority.

### 5. Resolve All Lexicons from an Authority

If you want to pull in **every** lexicon published by a given authority (or DID), use `LexiconResolveAuthority` instead of listing each NSID individually:

```xml
<ItemGroup>
  <LexiconResolveAuthority Include="blog.pckt" />
  <LexiconResolveAuthority Include="pub.leaflet" />
  <LexiconResolveAuthority Include="site.standard" />
</ItemGroup>
```

You can also pass a DID directly to skip the DNS lookup:

```xml
<ItemGroup>
  <LexiconResolveAuthority Include="did:plc:revjuqmkvrw6fnkxppqtszpv" />
</ItemGroup>
```

At build time, the authority is resolved to a DID (via DNS, unless a DID is provided directly), then `com.atproto.repo.listRecords` is used to enumerate all `com.atproto.lexicon.schema` records published by that identity. Each discovered lexicon is fetched, cached, and fed into the source generator.

This is useful for third-party or custom lexicon namespaces where you want everything the author publishes without maintaining an explicit list.

### 6. Resolve All Lexicons from an AT Protocol Handle

If you know someone's AT Protocol handle (e.g. their Bluesky handle), you can use `LexiconResolveHandle` to fetch all lexicons they've published:

```xml
<ItemGroup>
  <LexiconResolveHandle Include="atproto-lexicons.bsky.social" />
  <LexiconResolveHandle Include="bsky-lexicons.bsky.social" />
</ItemGroup>
```

At build time, the handle is resolved to a DID using standard AT Protocol handle resolution (DNS TXT `_atproto.{handle}` first, HTTPS `https://{handle}/.well-known/atproto-did` fallback), then the DID is resolved to a PDS endpoint, and `com.atproto.repo.listRecords` is used to enumerate all `com.atproto.lexicon.schema` records. Each discovered lexicon is fetched, cached, and fed into the source generator.

This differs from `LexiconResolveAuthority` in that it takes a user handle rather than an NSID authority or DID. Use this when you know an account's handle but not the authority namespace or DID they publish lexicons under.

`LexiconResolve`, `LexiconResolveAuthority`, `LexiconResolveHandle`, and `LexiconFiles` can all be combined freely:

```xml
<ItemGroup>
  <!-- Local files -->
  <LexiconFiles Include="lexicons/**/*.json" />

  <!-- Specific NSIDs -->
  <LexiconResolve Include="com.atproto.repo.listRecords" />

  <!-- All lexicons from these authorities -->
  <LexiconResolveAuthority Include="blog.pckt" />
  <LexiconResolveAuthority Include="site.standard" />

  <!-- All lexicons from these handles -->
  <LexiconResolveHandle Include="alice.bsky.social" />
</ItemGroup>
```

## MSBuild Properties

### Source Generator

| Property | Description | Default |
|---|---|---|
| `CarpaNet_SourceGen_RootNamespace` | Root namespace for generated code | Project's root namespace |
| `CarpaNet_JsonContextName` | Name for the generated `JsonSerializerContext` class | `ATProtoJsonContext` |
| `CarpaNet_CborContextName` | Name for the generated `CborSerializerContext` class | `ATProtoCborContext` |
| `CarpaNet_EmitValidationAttributes` | Emit validation attributes on generated properties | `false` |

### Lexicon Resolution

These properties configure the DNS-based lexicon resolution used with `<LexiconResolve>` and `<LexiconResolveAuthority>` items:

| Property | Description | Default |
|---|---|---|
| `CarpaNet_LexiconCacheDir` | Directory for caching resolved lexicon files | `$(BaseIntermediateOutputPath)lexicon-cache/` (i.e. `obj/lexicon-cache/`) |
| `CarpaNet_LexiconCacheTtlHours` | How long cached lexicons are valid (in hours). Set to `0` to force refresh. | `24` |
| `CarpaNet_LexiconFailOnError` | Whether resolution failures cause a build error (`true`) or warning (`false`) | `true` |
| `CarpaNet_PlcDirectoryUrl` | PLC directory URL for `did:plc` resolution | `https://plc.directory` |
| `CarpaNet_DnsServers` | Semicolon-separated DNS server IPs for TXT lookups | `1.1.1.1;8.8.8.8` |
| `CarpaNet_LexiconAutoResolve` | Automatically discover and resolve transitive lexicon dependencies at build time | `false` |
| `CarpaNet_LexiconAutoResolveMaxDepth` | Maximum number of iterations for transitive dependency resolution | `10` |

#### Automatic Transitive Resolution

When `CarpaNet_LexiconAutoResolve` is set to `true`, the build will automatically scan all known lexicon files for external NSID references and resolve any missing dependencies. This is repeated iteratively until all transitive dependencies are satisfied (or `MaxDepth` is reached).

This means you only need to list your top-level lexicons — all transitive deps are discovered and resolved automatically:

```xml
<PropertyGroup>
    <CarpaNet_LexiconAutoResolve>true</CarpaNet_LexiconAutoResolve>
</PropertyGroup>
<ItemGroup>
    <LexiconFiles Include="lexicons\com\whtwnd\**\*.json" />
    <LexiconResolve Include="app.bsky.feed.defs" />
    <!-- transitive dependencies are auto-resolved -->
</ItemGroup>
```

#### Caching

Resolved lexicons are cached on disk under `CarpaNet_LexiconCacheDir` with a configurable TTL. This avoids repeated network requests on every build.

- **Force refresh:** `dotnet build -p:CarpaNet_LexiconCacheTtlHours=0`
- **Clear cache:** delete the `obj/*/lexicon-cache/` directory

## What Gets Generated

### Data model classes

Each Lexicon `record`, `object`, and `token` definition becomes a C# class with `System.Text.Json` attributes. Classes are grouped by NSID namespace prefix into files like `AppBsky_Feed.g.cs`, `ComAtproto_Repo.g.cs`, etc.

Record types include a static `RecordType` string and are annotated with `[ATRecord]`.

### API extension methods (`ATProtoExtensions.g.cs`)

Lexicon `query` and `procedure` definitions become typed extension methods on `IATProtoClient`:

```csharp
// Generated from app.bsky.actor.getProfile query
await client.AppBskyActorGetProfileAsync(parameters);

// Generated from com.atproto.repo.createRecord procedure
await client.ComAtprotoRepoCreateRecordAsync(input);
```

Query parameters are gathered into a `*Parameters` class with a `ToDictionary()` method. Procedure inputs use an `*Input` class. Subscriptions generate `SubscribeAsync` extensions returning `IAsyncEnumerable<T>`.

### Union types (`UnionImplementations.g.cs`)

Lexicon `union` references become discriminated union classes with `System.Text.Json` polymorphic serialization via `[JsonPolymorphic]` and `[JsonDerivedType]` attributes.

### JSON serialization context (`ATProtoJsonContext.g.cs`)

A source-generated `JsonSerializerContext` that registers all generated types. The context name is configurable via `CarpaNet_JsonContextName`.

### CBOR serialization context (`ATProtoCborContext.g.cs`)

A `CborSerializerContext` that registers converters for all generated types, used for event stream deserialization and repository operations. The context name is configurable via `CarpaNet_CborContextName`.

### Serialization helpers (`SerializationMethods.g.cs`)

`ToJson()` and `FromJson()` methods on generated types for convenient serialization using the generated JSON context.

### Client factory (`ATProtoClientFactory.g.cs`)

A convenience factory class in the `CarpaNet` namespace that creates pre-configured `ATProtoClient` instances with the generated JSON and CBOR contexts already wired up.

```csharp
using CarpaNet;

// Uses generated contexts automatically
var client = ATProtoClientFactory.CreateSessionClient();
await client.LoginAsync("myhandle.bsky.social", "my-app-password");
```

## Inspecting Generated Code

Since this is a Roslyn generator, you can emit the compiled code to files so you can inspect them. It may also make it easier for LLMs to be able to debug and get the correct APIs. Set `EmitCompilerGeneratedFiles` in your project to write the generated files to disk:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Generated files appear under `obj/Debug/<tfm>/generated/CarpaNet.SourceGen/CarpaNet.LexiconGenerator/`.
