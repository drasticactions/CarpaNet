using System.Collections.Generic;

namespace CarpaNet.Identity;

/// <summary>
/// A method used to resolve a handle to a DID.
/// </summary>
public enum HandleResolutionMethod
{
    /// <summary>
    /// DNS TXT record at <c>_atproto.{handle}</c>, through the configured <see cref="IDnsResolver"/>.
    /// </summary>
    Dns,

    /// <summary>
    /// HTTPS request to <c>https://{handle}/.well-known/atproto-did</c>.
    /// </summary>
    WellKnown,

    /// <summary>
    /// XRPC call to <c>com.atproto.identity.resolveHandle</c> on
    /// <see cref="IdentityResolverOptions.HandleResolutionServiceUrl"/>.
    /// The result is only as trustworthy as that service (see <see cref="XrpcHandleResolver"/>).
    /// Skipped when no service URL is configured.
    /// </summary>
    Xrpc,
}

/// <summary>
/// Options for <see cref="IdentityResolver"/>.
/// </summary>
public sealed class IdentityResolverOptions
{
    /// <summary>
    /// Base URL of the public Bluesky AppView, which can be used as
    /// <see cref="HandleResolutionServiceUrl"/>.
    /// </summary>
    public const string PublicBlueskyAppViewUrl = "https://public.api.bsky.app";

    /// <summary>
    /// Gets or sets the PLC directory URL. If null, <see cref="IdentityResolver.DefaultPlcDirectory"/> is used.
    /// </summary>
    public string? PlcDirectoryUrl { get; set; }

    /// <summary>
    /// Gets or sets the DNS resolver for <see cref="HandleResolutionMethod.Dns"/>.
    /// If null, <see cref="DnsResolverDefaults.CreateDefault"/> is used.
    /// </summary>
    public IDnsResolver? DnsResolver { get; set; }

    /// <summary>
    /// Gets or sets the identity cache. If null, results are not cached.
    /// </summary>
    public IIdentityCache? Cache { get; set; }

    /// <summary>
    /// Gets or sets the handle resolution methods, in the order they are tried.
    /// The first method that returns a DID wins.
    /// If null or empty, <see cref="DefaultHandleResolutionOrder"/> is used.
    /// </summary>
    /// <example>
    /// To use only the XRPC service (for example in a browser):
    /// <code>
    /// new IdentityResolverOptions
    /// {
    ///     HandleResolutionServiceUrl = IdentityResolverOptions.PublicBlueskyAppViewUrl,
    ///     HandleResolutionOrder = new[] { HandleResolutionMethod.Xrpc },
    /// };
    /// </code>
    /// </example>
    public IReadOnlyList<HandleResolutionMethod>? HandleResolutionOrder { get; set; }

    /// <summary>
    /// Gets or sets the base URL of a service (a PDS or AppView, for example
    /// <see cref="PublicBlueskyAppViewUrl"/>) used for <see cref="HandleResolutionMethod.Xrpc"/>.
    /// If null, the XRPC method is skipped.
    /// </summary>
    /// <remarks>
    /// A DID returned by this service is not verified against the handle's DNS record or
    /// well-known file. It is only as trustworthy as the service. The DID document's handle
    /// claim is still checked by <see cref="IdentityResolver.ResolveAsync(string, System.Threading.CancellationToken)"/>.
    /// </remarks>
    public string? HandleResolutionServiceUrl { get; set; }

    /// <summary>
    /// Gets the default handle resolution order: DNS, then well-known, then XRPC
    /// (XRPC only when <see cref="HandleResolutionServiceUrl"/> is set).
    /// </summary>
    public static IReadOnlyList<HandleResolutionMethod> DefaultHandleResolutionOrder { get; } = new[]
    {
        HandleResolutionMethod.Dns,
        HandleResolutionMethod.WellKnown,
        HandleResolutionMethod.Xrpc,
    };
}
