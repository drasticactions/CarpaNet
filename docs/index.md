# CarpaNet - a .NET ATProtocol Library

[![NuGet Version](https://img.shields.io/nuget/v/CarpaNet.svg)](https://www.nuget.org/packages/CarpaNet/) ![License](https://img.shields.io/badge/License-MIT-blue.svg)

CarpaNet is a .NET library for [ATProtocol](https://atproto.com/) that uses Roslyn source generators to produce type-safe API bindings from Lexicon JSON schema files. It's the replacement for [FishyFlip](https://github.com/drasticactions/FishyFlip).

![1444070256569233](https://user-images.githubusercontent.com/898335/167266846-1ad2648f-91c1-4a04-a18d-6dd4d6c7d21c.gif)

This site is under construction.

## Quick Start

Add the CarpaNet NuGet package and declare which lexicons you need:

```xml
<ItemGroup>
  <PackageReference Include="CarpaNet" Version="1.*" />
</ItemGroup>

<ItemGroup>
  <LexiconResolve Include="app.bsky.actor.getProfile" />
  <LexiconResolve Include="app.bsky.feed.post" />
  <LexiconResolve Include="com.atproto.repo.createRecord" />
</ItemGroup>
```

Then use the generated client:

```csharp
using CarpaNet;

var client = ATProtoClientFactory.Create();

var profile = await client.AppBskyActorGetProfileAsync(
    new AppBsky.Actor.GetProfileParameters { Actor = new ATHandle("alice.bsky.social") });

Console.WriteLine($"{profile.DisplayName} (@{profile.Handle})");
```

## Packages

| Package | Description |
|---------|-------------|
| [CarpaNet](https://www.nuget.org/packages/CarpaNet/) | Core runtime, source generator, XRPC protocol, identity resolution, DAG-CBOR, CAR files |
| [CarpaNet.OAuth](https://www.nuget.org/packages/CarpaNet.OAuth/) | OAuth 2.0 with PAR, PKCE, and DPoP support |
| [CarpaNet.Jetstream](https://www.nuget.org/packages/CarpaNet.Jetstream/) | Real-time event subscription via Bluesky Jetstream |
| [CarpaNet.AspNetCore](https://www.nuget.org/packages/CarpaNet.AspNetCore/) | ASP.NET Core integration |

## Third-Party Libraries

- [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp) — Zstandard compression
