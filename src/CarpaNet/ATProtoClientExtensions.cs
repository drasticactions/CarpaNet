using CarpaNet.Identity;

namespace CarpaNet;

/// <summary>
/// Extension methods for resolving AT URI authorities to PDS endpoints and NSID routing.
/// </summary>
public static class ATProtoClientExtensions
{
    /// <summary>
    /// Gets the service category for an NSID, which helps determine routing.
    /// </summary>
    /// <param name="nsid">The NSID to categorize.</param>
    /// <returns>The service category.</returns>
    public static NsidServiceCategory GetServiceCategory(string nsid)
    {
        if (string.IsNullOrEmpty(nsid))
            return NsidServiceCategory.Unknown;

        if (nsid.StartsWith("com.atproto.", StringComparison.Ordinal))
            return NsidServiceCategory.AtProto;

        if (nsid.StartsWith("app.bsky.", StringComparison.Ordinal))
            return NsidServiceCategory.BlueskyApp;

        if (nsid.StartsWith("chat.bsky.", StringComparison.Ordinal))
            return NsidServiceCategory.BlueskyChat;

        if (nsid.StartsWith("tools.ozone.", StringComparison.Ordinal))
            return NsidServiceCategory.Ozone;

        return NsidServiceCategory.Unknown;
    }

    /// <summary>
    /// Determines if an NSID represents a write operation (typically requires authentication).
    /// </summary>
    /// <param name="nsid">The NSID to check.</param>
    /// <returns>True if the NSID is typically a write operation.</returns>
    public static bool IsWriteOperation(string nsid)
    {
        if (string.IsNullOrEmpty(nsid))
            return false;

        // Repository operations that modify data
        if (nsid.StartsWith("com.atproto.repo.", StringComparison.Ordinal))
        {
            return nsid.Contains("create") ||
                   nsid.Contains("put") ||
                   nsid.Contains("delete") ||
                   nsid.Contains("apply");
        }

        // Most procedures (POST endpoints) that aren't explicitly reads
        // This is a heuristic - specific endpoints may differ
        return false;
    }

    /// <summary>
    /// Gets the recommended proxy service DID for an NSID when making authenticated requests through a PDS.
    /// </summary>
    /// <param name="nsid">The NSID.</param>
    /// <returns>The proxy service DID, or null if no proxy is needed.</returns>
    /// <remarks>
    /// For most app.bsky.* endpoints, the PDS handles proxying automatically.
    /// This is primarily needed for chat.bsky.* and tools.ozone.* endpoints.
    /// </remarks>
    public static string? GetProxyServiceDid(string nsid)
    {
        var category = GetServiceCategory(nsid);
        return category switch
        {
            NsidServiceCategory.BlueskyChat => BlueskyServices.ChatServiceDid,
            NsidServiceCategory.Ozone => BlueskyServices.OzoneServiceDid,
            _ => null // PDS handles app.bsky.* and com.atproto.* automatically
        };
    }

    /// <summary>
    /// Resolves the authority (DID or handle) from an AT URI to get the PDS endpoint URL.
    /// </summary>
    /// <param name="client">The ATProto client.</param>
    /// <param name="uri">The AT URI to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The PDS endpoint URL for the authority.</returns>
    /// <exception cref="InvalidOperationException">If the client has no identity resolver configured.</exception>
    /// <exception cref="IdentityResolutionException">If the authority cannot be resolved.</exception>
    public static async Task<string> ResolveAuthorityAsync(
        this IATProtoClient client,
        ATUri uri,
        CancellationToken cancellationToken = default)
    {
        if (client.IdentityResolver == null)
        {
            throw new InvalidOperationException(
                "Cannot resolve AT URI authority: no IdentityResolver configured on the client.");
        }

        var authority = uri.Authority;
        if (string.IsNullOrEmpty(authority))
        {
            throw new ArgumentException("AT URI has no authority.", nameof(uri));
        }

        var didDoc = await client.IdentityResolver.ResolveAsync(authority!, cancellationToken).ConfigureAwait(false);

        var pdsEndpoint = didDoc.PdsEndpoint;
        if (string.IsNullOrEmpty(pdsEndpoint))
        {
            throw new IdentityResolutionException(
                $"No PDS endpoint found in DID document for '{authority}'.");
        }

        return pdsEndpoint!;
    }

    /// <summary>
    /// Resolves the authority (DID or handle) from an AT URI to get the full DID document.
    /// </summary>
    /// <param name="client">The ATProto client.</param>
    /// <param name="uri">The AT URI to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The DID document for the authority.</returns>
    /// <exception cref="InvalidOperationException">If the client has no identity resolver configured.</exception>
    /// <exception cref="IdentityResolutionException">If the authority cannot be resolved.</exception>
    public static async Task<DidDocument> ResolveAuthorityToDidDocumentAsync(
        this IATProtoClient client,
        ATUri uri,
        CancellationToken cancellationToken = default)
    {
        if (client.IdentityResolver == null)
        {
            throw new InvalidOperationException(
                "Cannot resolve AT URI authority: no IdentityResolver configured on the client.");
        }

        var authority = uri.Authority;
        if (string.IsNullOrEmpty(authority))
        {
            throw new ArgumentException("AT URI has no authority.", nameof(uri));
        }

        return await client.IdentityResolver.ResolveAsync(authority!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves an AT URI authority using the provided identity resolver.
    /// </summary>
    /// <param name="uri">The AT URI to resolve.</param>
    /// <param name="resolver">The identity resolver to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The PDS endpoint URL for the authority.</returns>
    public static async Task<string> ResolvePdsEndpointAsync(
        this ATUri uri,
        IdentityResolver resolver,
        CancellationToken cancellationToken = default)
    {
        if (resolver == null)
        {
            throw new ArgumentNullException(nameof(resolver));
        }

        var authority = uri.Authority;
        if (string.IsNullOrEmpty(authority))
        {
            throw new ArgumentException("AT URI has no authority.", nameof(uri));
        }

        var didDoc = await resolver.ResolveAsync(authority!, cancellationToken).ConfigureAwait(false);

        var pdsEndpoint = didDoc.PdsEndpoint;
        if (string.IsNullOrEmpty(pdsEndpoint))
        {
            throw new IdentityResolutionException(
                $"No PDS endpoint found in DID document for '{authority}'.");
        }

        return pdsEndpoint!;
    }
}
