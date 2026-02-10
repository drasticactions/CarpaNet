using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Identity;

namespace CarpaNet;

/// <summary>
/// Interface for ATProtocol client implementations.
/// Generated API extension methods target this interface.
/// </summary>
/// <remarks>
/// <para>
/// ATProto has a distributed architecture with different services handling different APIs:
/// </para>
/// <list type="bullet">
/// <item><description>PDS (Personal Data Server): User's data home - all writes, account management</description></item>
/// <item><description>AppView: Handles app.bsky.* endpoints - can be accessed via PDS proxy or directly</description></item>
/// <item><description>Relay: Provides firehose subscription for network-wide events</description></item>
/// </list>
/// <para>
/// For authenticated clients, requests typically go through the user's PDS, which proxies
/// to other services as needed. For public requests, clients can hit the public AppView directly.
/// </para>
/// </remarks>
public interface IATProtoClient
{
    /// <summary>
    /// Gets the base URL for API requests.
    /// For authenticated clients, this is typically the user's PDS.
    /// For public clients, this might be the public AppView.
    /// </summary>
    Uri BaseUrl { get; }

    /// <summary>
    /// Gets whether this client is authenticated.
    /// Authenticated clients can make write requests and access private data.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the DID of the authenticated user, or null if not authenticated.
    /// </summary>
    string? AuthenticatedDid { get; }

    /// <summary>
    /// Gets the identity resolver used for resolving handles and DIDs.
    /// Returns null if identity resolution is not configured.
    /// </summary>
    IdentityResolver? IdentityResolver { get; }

    /// <summary>
    /// Performs an XRPC GET request (query).
    /// </summary>
    /// <typeparam name="TOutput">The expected output type.</typeparam>
    /// <param name="nsid">The NSID of the endpoint.</param>
    /// <param name="parameters">Optional query parameters as key-value pairs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    Task<TOutput> GetAsync<TOutput>(
        string nsid,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs an XRPC GET request with service proxying.
    /// </summary>
    /// <typeparam name="TOutput">The expected output type.</typeparam>
    /// <param name="nsid">The NSID of the endpoint.</param>
    /// <param name="proxyServiceDid">The service DID to proxy to (sets atproto-proxy header).</param>
    /// <param name="parameters">Optional query parameters as key-value pairs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    /// <remarks>
    /// Use this for requests that need to be proxied through the PDS to another service.
    /// For example, chat.bsky.* requests need to be proxied to the chat service.
    /// </remarks>
    Task<TOutput> GetAsync<TOutput>(
        string nsid,
        string proxyServiceDid,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs an XRPC POST request (procedure).
    /// </summary>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <typeparam name="TOutput">The expected output type.</typeparam>
    /// <param name="nsid">The NSID of the endpoint.</param>
    /// <param name="input">The request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        TInput? input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs an XRPC POST request with service proxying.
    /// </summary>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <typeparam name="TOutput">The expected output type.</typeparam>
    /// <param name="nsid">The NSID of the endpoint.</param>
    /// <param name="proxyServiceDid">The service DID to proxy to (sets atproto-proxy header).</param>
    /// <param name="input">The request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        string proxyServiceDid,
        TInput? input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to an event stream (WebSocket).
    /// </summary>
    /// <typeparam name="TMessage">The message union type.</typeparam>
    /// <param name="nsid">The NSID of the subscription endpoint.</param>
    /// <param name="parameters">Optional query parameters as key-value pairs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of messages.</returns>
    IAsyncEnumerable<TMessage> SubscribeAsync<TMessage>(
        string nsid,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default);
}