# Project Setup

## Prerequisites

- .NET 8 SDK or above
- `dotnet add package CarpaNet`

## Minimal .csproj

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

## Lexicon Sources

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

## Auto-Resolve Transitive Dependencies

Enable automatic discovery of referenced lexicons you haven't explicitly listed:

```xml
<PropertyGroup>
  <CarpaNet_LexiconAutoResolve>true</CarpaNet_LexiconAutoResolve>
</PropertyGroup>
```

This scans your lexicons for `ref` fields pointing to external NSIDs, resolves them via DNS, and repeats until all dependencies are satisfied.

## MSBuild Properties

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

## Inspecting Generated Code

Roslyn allows for emiting the compiler generated files. This makes it easy to debug (and for LLMs to inspect, as it were.)

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Generated files appear at `obj/Debug/{TFM}/generated/CarpaNet.SourceGen/CarpaNet.LexiconGenerator/`.
