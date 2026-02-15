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

## MSBuild Properties

| Property | Description | Default |
|---|---|---|
| `CarpaNet_SourceGen_RootNamespace` | Root namespace for generated code | Project's root namespace |
| `CarpaNet_JsonContextName` | Name for the generated `JsonSerializerContext` class | `ATProtoJsonContext` |
| `CarpaNet_CborContextName` | Name for the generated `CborSerializerContext` class | `ATProtoCborContext` |
| `CarpaNet_EmitValidationAttributes` | Emit validation attributes on generated properties | `false` |

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
