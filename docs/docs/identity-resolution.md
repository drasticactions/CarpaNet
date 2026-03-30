# Identity Resolution

Resolve handles to DIDs and DID documents:

```csharp
using CarpaNet.Identity;

// Create with in-memory caching
var resolver = IdentityResolver.CreateWithCache();

// Handle → DID document
var didDoc = await resolver.ResolveAsync("alice.bsky.social");
Console.WriteLine($"DID: {didDoc.Id}");
Console.WriteLine($"PDS: {didDoc.PdsEndpoint}");
Console.WriteLine($"Handle: {didDoc.Handle}");

// DID → DID document
var didDoc2 = await resolver.ResolveAsync("did:plc:z72i7hdynmk6r22z27h6tvur");
```

The `ATProtoClient` creates an `IdentityResolver` automatically (configurable via `ATProtoClientOptions.CreateIdentityResolver`).
