# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build (requires .NET SDK 10.0+)
dotnet build CarpaNet.slnx

# Run tests
dotnet test tests/CarpaNet.UnitTests/CarpaNet.UnitTests.csproj

# Run a single test
dotnet test tests/CarpaNet.UnitTests/CarpaNet.UnitTests.csproj --filter "FullyQualifiedName~TestClassName.TestMethodName"

# Pack NuGet packages
dotnet pack src/CarpaNet/CarpaNet.csproj --configuration Release --output nupkg
dotnet pack src/CarpaNet.OAuth/CarpaNet.OAuth.csproj --configuration Release --output nupkg
dotnet pack src/CarpaNet.Jetstream/CarpaNet.Jetstream.csproj --configuration Release --output nupkg

# Build samples (separate solution)
dotnet build CarpaNet.Samples.slnx
```

## Architecture

CarpaNet is a .NET ATProtocol library that uses Roslyn source generators to produce API bindings from Lexicon JSON schema files. It is designed for full NativeAOT compatibility with minimal dependencies.

### Project Layout

- **src/CarpaNet** - Core runtime: `IATProtoClient`/`ATProtoClient`, XRPC protocol, identity resolution, DAG-CBOR serialization, CAR file reading, event streams
- **src/CarpaNet.SourceGen** - Roslyn incremental source generator (`LexiconGenerator`). Parses Lexicon JSON files and generates C# records, API extension methods, union types, and JSON/CBOR serialization contexts
- **src/CarpaNet.BuildTasks** - MSBuild tasks for DNS-based lexicon resolution at build time (resolving NSIDs, authorities, handles to lexicon files)
- **src/CarpaNet.OAuth** - OAuth 2.0 with PAR, PKCE, and DPoP support via `OAuthSession`
- **src/CarpaNet.Jetstream** - Real-time event subscription via Bluesky Jetstream (Zstandard-compressed WebSocket)

### Source Generator Pipeline

The source generator (`LexiconGenerator`) is the core architectural piece:
1. MSBuild targets (`CarpaNet.targets`) resolve lexicon files via DNS/authority/handle lookup and mark them as `AdditionalFiles` with `IsATProtoLexicon` metadata
2. `LexiconParser` converts JSON to `LexiconDocument` AST
3. `TypeRegistry` maps lexicon definitions to C# types
4. Generators emit: data model records (`ObjectGenerator`), client extension methods (`ApiGenerator`), discriminated unions (`UnionGenerator`), JSON context (`JsonContextGenerator`), CBOR context (`CborContextGenerator`)

Consumer projects include lexicons via MSBuild items (`LexiconFiles`, `LexiconResolve`, `LexiconResolveAuthority`, `LexiconResolveHandle`) and the generator produces all bindings at compile time.

### Key Patterns

- **Multi-targeting**: Core libraries target net8.0/net9.0/net10.0/netstandard2.0. Source generator targets netstandard2.0 only. Tests target net10.0.
- **Central Package Management**: All package versions in `Directory.Packages.props`
- **Strong naming**: Assemblies signed with `Key.snk`
- **GitVersion**: Semantic versioning via `Version.props`
- **Token providers**: Authentication abstracted via `ITokenProvider` (session-based or DPoP)
- **No reflection**: All serialization uses source-generated contexts (System.Text.Json and System.Formats.Cbor)

### Design Principles

- Minimal runtime dependencies (only ZstdSharp.Port beyond framework libs)
- NativeAOT compatible - no JIT-time reflection
- ATProtocol-focused, not Bluesky-specific
- Simple APIs - complexity lives in the source generator, not the consumer API surface

## Branching

- `main` - stable releases
- `develop` - active development, NuGet auto-publishes from this branch when src/ or tests/ change
