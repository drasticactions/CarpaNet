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

## Handle resolution methods

A handle is resolved to a DID with these methods. They are tried in order, and the first method that returns a DID wins:

1. **DNS** – the TXT record at `_atproto.<handle>`, through an `IDnsResolver`.
2. **Well-known** – `https://<handle>/.well-known/atproto-did`.
3. **XRPC** – `com.atproto.identity.resolveHandle` on a service that you configure, such as your PDS or `https://public.api.bsky.app`. This method is skipped if no service URL is set.

Use `IdentityResolverOptions` to change the order or to add the XRPC service:

```csharp
var resolver = new IdentityResolver(httpClient, new IdentityResolverOptions
{
    Cache = new MemoryIdentityCache(),
    HandleResolutionServiceUrl = IdentityResolverOptions.PublicBlueskyAppViewUrl,
    // Optional. The default order is Dns, WellKnown, Xrpc.
    HandleResolutionOrder = new[] { HandleResolutionMethod.Xrpc },
});
```

> **Trust:** A DID from the XRPC method is only as trustworthy as the service. The client does not check the handle's DNS record or well-known file. `ResolveAsync` still checks that the DID document claims the handle (`alsoKnownAs`), for all methods.

## DNS resolvers

| Resolver | Transport | Use |
| --- | --- | --- |
| `DefaultDnsResolver` | Raw UDP to `1.1.1.1` and `8.8.8.8` | Desktop, server and mobile |
| `DnsOverHttpsResolver` | HTTPS JSON API (`application/dns-json`) to Cloudflare, then Google | Browsers (WebAssembly), and networks that block UDP |

If you do not give a DNS resolver, `IdentityResolver` calls `DnsResolverDefaults.CreateDefault(httpClient)`. This returns `DnsOverHttpsResolver` in a browser or under WASI, and `DefaultDnsResolver` on other platforms.

```csharp
// Force DNS-over-HTTPS, with custom endpoints and a 3-second timeout for each endpoint
var dns = new DnsOverHttpsResolver(
    httpClient,
    new[] { DnsOverHttpsResolver.GoogleEndpoint, DnsOverHttpsResolver.CloudflareEndpoint },
    TimeSpan.FromSeconds(3));
var resolver = new IdentityResolver(httpClient, new IdentityResolverOptions { DnsResolver = dns });
```

`DnsOverHttpsResolver` tries the next endpoint if a request fails, times out, returns malformed JSON or returns a DNS error such as SERVFAIL. An NXDOMAIN answer returns an empty list.

### Browsers

In a browser, the well-known request is usually blocked by CORS. For reliable handle resolution, set `HandleResolutionServiceUrl` to your PDS or to an AppView.
