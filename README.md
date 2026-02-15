# CarpaNet

CarpaNet is a .NET Library for interacting with [ATProtocol](https://atproto.com). It uses Roslyn source generators to produce API bindings, serialization contexts, and data models from ATProtocol Lexicon JSON files, designed for full AOT compatibility.

For information on the core CarpaNet library, check out its [Readme](./src/CarpaNet/README.md).
For information on the OAuth support, check out [CarpaNet.Oauth](./src/CarpaNet.OAuth/README.md).
For information on the Jetstream support, check out [CarpaNet.Jetstream](./src/CarpaNet.Jetstream/README.md).
For information on the Source Generator, check out its [Readme](./src/CarpaNet.SourceGen/README.md).

![1444070256569233](https://user-images.githubusercontent.com/898335/167266846-1ad2648f-91c1-4a04-a18d-6dd4d6c7d21c.gif)

This library is experimental and not stable. Expect issues and bugs!

## **NOTE**

- To run the samples and tests, you need to set the `ATPROTO_LEXICON` env to a checkout of [atproto](https://github.com/bluesky-social/atproto), else it won't work. It is not required to build the libraries.
- Issues and PRs are fine, however I offer no support for this library. If you find yourself dependent on it for something super important, you should probably fork it.

# Comparison to FishyFlip

[FishyFlip](https://github.com/drasticactions/FishyFlip) was my original ATProtocol/Bluesky library for .NET. I originally intended it as a client SDK for Bluesky, but over the years, it evolved into a framework with a source generator and many helper extensions. However, this led to issues:

- While I wrote a [source generator](https://github.com/drasticactions/FishyFlip/tree/develop/tools/FFSourceGen) that could read ATProtocol Lexicon files and produce valid classes, I used it to bind them as base types in FishyFlip itself. By doing that, I limited you to whatever version of the lexicon types I happened to bind in code for ATProtocol and Bluesky. As these types are frequently updated, I would need to kick off new builds and push releases, which made it impossible to maintain a "stable" version of the library.
- The source generator codebase was very much hacked together and held together by hopes and dreams. ATProtocol types and .NET primitives and namespaces often collide, so I needed to create a system that would make it both parsable and friendly to a C# .NET Developer. As a result, I often had to add more fixes and hacks on top to keep it running with the existing protocol or with third-party lexicons. I could never get it stable enough to ship on its own.
- There are a few third-party dependencies I used to make it easier for me to write against, such as Ipfs.Cid and Peter.CBOR. The runtime could handle some of these functions, or I could add a small amount of custom code that I could maintain.
- The pattern-matching `Result` object for returning success or error types from endpoints was, I feel, too clever. I iterated on it several times to make it easier to understand and deconstruct. Still, in code from people using the library, I often saw them casting to or using the `T0` and `T1` types to check against. Whenever I tried using LLMs against FishyFlip, they often failed to generate the correct output or would do bad practices.

With that, the goals of CarpaNet are to:

- Reduce the dependency list. Stick as much as possible to only libraries in the runtime.
- Maintain NativeAOT compatibility. No exceptions.
- Reduce complexity and avoid "clever" SDK methods; keep it simple. Leave the hard stuff to the source generator. You shouldn't need to learn new concepts to use this.
- Keep it ATProtocol, and don't turn this into a Bluesky-specific library. The core CarpaNet library should not include helper methods specific to using Bluesky - The social network. It should stick to making it easier to interoperate with ATProtocol.

### Third-Party Libraries

- [GitVersion](https://github.com/GitTools/GitVersion)
- [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp)