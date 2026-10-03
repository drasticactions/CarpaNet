using System;
using System.Collections.Generic;

namespace CarpaNet;

/// <summary>
/// Per-request settings for an XRPC call: service proxying, accepted labelers, extra headers,
/// and an alternate service to send the request to.
/// </summary>
/// <remarks>
/// <para>
/// Options are usually applied to many calls at once with
/// <see cref="ATProtoClientXrpcExtensions.WithRequestOptions(IATProtoClient, XrpcRequestOptions)"/>,
/// which returns a client whose generated API methods all use them.
/// </para>
/// <para>
/// Credentials are only attached when a request goes to the authenticated session's own PDS.
/// A request sent to <see cref="ServiceUrl"/> carries no session credentials; put any
/// <c>Authorization</c> it needs (such as a service-auth token) in <see cref="Headers"/>.
/// </para>
/// </remarks>
public sealed class XrpcRequestOptions
{
    /// <summary>
    /// Gets or sets the service DID reference sent in the <c>atproto-proxy</c> header
    /// (for example <c>did:web:api.bsky.app#bsky_appview</c>).
    /// When set, it replaces the proxy a generated method would use.
    /// </summary>
    public string? ProxyServiceDid { get; set; }

    /// <summary>
    /// Gets or sets whether the request is sent without an <c>atproto-proxy</c> header,
    /// even when a generated method would add one. Takes precedence over <see cref="ProxyServiceDid"/>.
    /// </summary>
    public bool DisableProxy { get; set; }

    /// <summary>
    /// Gets or sets the labeler DIDs sent in the <c>atproto-accept-labelers</c> header,
    /// replacing the client's own list. Entries may carry parameters such as <c>;redact</c>
    /// (see <see cref="AcceptLabelersHeader.Redact(string)"/>). An empty list sends no header.
    /// </summary>
    public IReadOnlyList<string>? AcceptLabelers { get; set; }

    /// <summary>
    /// Gets or sets additional request headers. These are added after the XRPC headers and
    /// may include <c>Authorization</c>, in which case session credentials are not attached.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Gets or sets the base URL of a service to send the request to instead of the session's PDS.
    /// Requests to this service never carry session credentials and are not re-routed by the
    /// <c>repo</c> parameter.
    /// </summary>
    public Uri? ServiceUrl { get; set; }

    /// <summary>
    /// Gets the proxy to send, taking <see cref="DisableProxy"/> into account.
    /// </summary>
    public string? EffectiveProxyServiceDid => DisableProxy ? null : ProxyServiceDid;

    /// <summary>
    /// Gets whether these options set the proxy, either to a service or to none.
    /// </summary>
    internal bool SetsProxy => DisableProxy || ProxyServiceDid != null;

    /// <summary>
    /// Combines two sets of options. Values set on <paramref name="outer"/> win;
    /// headers are merged, with <paramref name="outer"/> winning on the same name.
    /// </summary>
    /// <param name="outer">The options that take precedence.</param>
    /// <param name="inner">The options used for anything <paramref name="outer"/> does not set.</param>
    /// <returns>The combined options, or null when both are null.</returns>
    public static XrpcRequestOptions? Combine(XrpcRequestOptions? outer, XrpcRequestOptions? inner)
    {
        if (outer == null)
        {
            return inner;
        }

        if (inner == null)
        {
            return outer;
        }

        var outerSetsProxy = outer.SetsProxy;
        return new XrpcRequestOptions
        {
            ProxyServiceDid = outerSetsProxy ? outer.ProxyServiceDid : inner.ProxyServiceDid,
            DisableProxy = outerSetsProxy ? outer.DisableProxy : inner.DisableProxy,
            AcceptLabelers = outer.AcceptLabelers ?? inner.AcceptLabelers,
            Headers = MergeHeaders(outer.Headers, inner.Headers),
            ServiceUrl = outer.ServiceUrl ?? inner.ServiceUrl,
        };
    }

    private static IReadOnlyDictionary<string, string>? MergeHeaders(
        IReadOnlyDictionary<string, string>? outer,
        IReadOnlyDictionary<string, string>? inner)
    {
        if (outer == null || outer.Count == 0)
        {
            return inner;
        }

        if (inner == null || inner.Count == 0)
        {
            return outer;
        }

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in inner)
        {
            merged[header.Key] = header.Value;
        }

        foreach (var header in outer)
        {
            merged[header.Key] = header.Value;
        }

        return merged;
    }
}

/// <summary>
/// Helpers for the <c>atproto-accept-labelers</c> header.
/// </summary>
public static class AcceptLabelersHeader
{
    /// <summary>
    /// The header name.
    /// </summary>
    public const string Name = "atproto-accept-labelers";

    /// <summary>
    /// Returns the header entry for a labeler whose takedown-level labels should be applied
    /// by the AppView (redacting the content), for example <c>did:plc:abc;redact</c>.
    /// </summary>
    /// <param name="labelerDid">The labeler DID.</param>
    /// <returns>The DID with the <c>;redact</c> parameter.</returns>
    public static string Redact(string labelerDid)
    {
        if (string.IsNullOrEmpty(labelerDid))
        {
            throw new ArgumentException("Labeler DID cannot be null or empty.", nameof(labelerDid));
        }

        return labelerDid + ";redact";
    }
}
