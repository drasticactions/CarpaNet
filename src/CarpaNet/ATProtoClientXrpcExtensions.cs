using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Http;

namespace CarpaNet;

/// <summary>
/// XRPC calls beyond the JSON query/procedure methods on <see cref="IATProtoClient"/>:
/// binary bodies, binary responses, procedures with query parameters, and per-request options.
/// </summary>
/// <remarks>
/// These require a client that implements <see cref="IXrpcRequestClient"/>
/// (<see cref="ATProtoClient"/>, the OAuth client and <see cref="ScopedATProtoClient"/> do).
/// Generated API methods call the first three methods.
/// </remarks>
public static class ATProtoClientXrpcExtensions
{
    #region Used by generated code

    /// <summary>
    /// Calls a procedure that has a JSON body and query parameters.
    /// </summary>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <typeparam name="TOutput">The output type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="nsid">The NSID of the procedure.</param>
    /// <param name="proxyServiceDid">The service to proxy to, or null.</param>
    /// <param name="parameters">The query parameters.</param>
    /// <param name="input">The request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<TOutput> PostWithParametersAsync<TInput, TOutput>(
        this IATProtoClient client,
        string nsid,
        string? proxyServiceDid,
        IEnumerable<KeyValuePair<string, string>>? parameters,
        TInput? input,
        CancellationToken cancellationToken = default)
    {
        if (client is IXrpcRequestClient xrpc)
        {
            return xrpc.ProcedureAsync<TInput, TOutput>(nsid, parameters, input, ProxyOptions(proxyServiceDid), cancellationToken);
        }

        if (parameters == null || !parameters.Any())
        {
            return proxyServiceDid == null
                ? client.PostAsync<TInput, TOutput>(nsid, input, cancellationToken)
                : client.PostAsync<TInput, TOutput>(nsid, proxyServiceDid, input, cancellationToken);
        }

        throw NotSupported(client, "procedures with query parameters");
    }

    /// <summary>
    /// Calls a procedure whose body is not JSON (for example <c>com.atproto.repo.uploadBlob</c>).
    /// </summary>
    /// <typeparam name="TOutput">The output type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="nsid">The NSID of the procedure.</param>
    /// <param name="proxyServiceDid">The service to proxy to, or null.</param>
    /// <param name="parameters">The query parameters, or null.</param>
    /// <param name="body">The body. It is read from its current position and not disposed.</param>
    /// <param name="contentType">The MIME type of the body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<TOutput> PostBinaryAsync<TOutput>(
        this IATProtoClient client,
        string nsid,
        string? proxyServiceDid,
        IEnumerable<KeyValuePair<string, string>>? parameters,
        Stream body,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var xrpc = AsXrpcClient(client, "binary request bodies");
        return xrpc.ProcedureBinaryAsync<TOutput>(nsid, parameters, body, contentType, ProxyOptions(proxyServiceDid), cancellationToken);
    }

    /// <summary>
    /// Calls a query whose response is not JSON (for example <c>com.atproto.sync.getBlob</c>).
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="nsid">The NSID of the query.</param>
    /// <param name="proxyServiceDid">The service to proxy to, or null.</param>
    /// <param name="parameters">The query parameters, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response body.</returns>
    public static Task<byte[]> GetBytesAsync(
        this IATProtoClient client,
        string nsid,
        string? proxyServiceDid,
        IEnumerable<KeyValuePair<string, string>>? parameters,
        CancellationToken cancellationToken = default)
    {
        var xrpc = AsXrpcClient(client, "binary responses");
        return xrpc.QueryBytesAsync(nsid, parameters, ProxyOptions(proxyServiceDid), cancellationToken);
    }

    #endregion

    #region Request helpers

    /// <summary>
    /// Calls a query and deserializes its JSON response.
    /// </summary>
    public static async Task<TOutput> QueryAsync<TOutput>(
        this IXrpcRequestClient client,
        string nsid,
        IEnumerable<KeyValuePair<string, string>>? parameters,
        XrpcRequestOptions? options,
        CancellationToken cancellationToken = default)
    {
        var request = new XrpcRequest(HttpMethod.Get, nsid) { Parameters = parameters, Options = options };
        using var response = await client.SendXrpcAsync(request, cancellationToken).ConfigureAwait(false);
        return await XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, client.JsonOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calls a procedure with an optional JSON body and deserializes its JSON response.
    /// </summary>
    public static async Task<TOutput> ProcedureAsync<TInput, TOutput>(
        this IXrpcRequestClient client,
        string nsid,
        IEnumerable<KeyValuePair<string, string>>? parameters,
        TInput? input,
        XrpcRequestOptions? options,
        CancellationToken cancellationToken = default)
    {
        var request = new XrpcRequest(HttpMethod.Post, nsid) { Parameters = parameters, Options = options };
        if (input != null)
        {
            var typeInfo = (JsonTypeInfo<TInput>)client.JsonOptions.GetTypeInfo(typeof(TInput));
            request.Body = XrpcBody.FromJson(input, typeInfo);
        }

        using var response = await client.SendXrpcAsync(request, cancellationToken).ConfigureAwait(false);
        return await XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, client.JsonOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calls a procedure with a binary body and deserializes its JSON response.
    /// </summary>
    public static async Task<TOutput> ProcedureBinaryAsync<TOutput>(
        this IXrpcRequestClient client,
        string nsid,
        IEnumerable<KeyValuePair<string, string>>? parameters,
        Stream body,
        string contentType,
        XrpcRequestOptions? options,
        CancellationToken cancellationToken = default)
    {
        var request = new XrpcRequest(HttpMethod.Post, nsid)
        {
            Parameters = parameters,
            Body = XrpcBody.FromStream(body, contentType),
            Options = options,
        };

        using var response = await client.SendXrpcAsync(request, cancellationToken).ConfigureAwait(false);
        return await XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, client.JsonOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calls a query and returns its response body as bytes.
    /// </summary>
    public static async Task<byte[]> QueryBytesAsync(
        this IXrpcRequestClient client,
        string nsid,
        IEnumerable<KeyValuePair<string, string>>? parameters,
        XrpcRequestOptions? options,
        CancellationToken cancellationToken = default)
    {
        var acceptAny = new XrpcRequestOptions
        {
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Accept"] = "*/*" },
        };

        var request = new XrpcRequest(HttpMethod.Get, nsid)
        {
            Parameters = parameters,
            Options = XrpcRequestOptions.Combine(options, acceptAny),
        };

        using var response = await client.SendXrpcAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await XrpcHttpHandler.ThrowForErrorResponseAsync(response, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

#if NET8_0_OR_GREATER
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
    }

    #endregion

    #region Scoping

    /// <summary>
    /// Returns a client that applies <paramref name="options"/> to every request, including the
    /// generated API methods. Options set here override a generated method's default proxy.
    /// </summary>
    /// <param name="client">The client to wrap.</param>
    /// <param name="options">The options to apply.</param>
    /// <returns>A client that shares <paramref name="client"/>'s session and HTTP pipeline.</returns>
    public static ScopedATProtoClient WithRequestOptions(this IATProtoClient client, XrpcRequestOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (client is ScopedATProtoClient scoped)
        {
            return new ScopedATProtoClient(scoped.Inner, XrpcRequestOptions.Combine(options, scoped.Options)!);
        }

        return new ScopedATProtoClient(client, options);
    }

    /// <summary>
    /// Returns a client that proxies every request to <paramref name="serviceDid"/>
    /// (for example <c>did:web:api.bsky.app#bsky_appview</c>).
    /// </summary>
    public static ScopedATProtoClient WithProxy(this IATProtoClient client, string serviceDid)
    {
        if (string.IsNullOrEmpty(serviceDid))
        {
            throw new ArgumentException("Service DID cannot be null or empty.", nameof(serviceDid));
        }

        return client.WithRequestOptions(new XrpcRequestOptions { ProxyServiceDid = serviceDid });
    }

    /// <summary>
    /// Returns a client that sends every request without an <c>atproto-proxy</c> header,
    /// so the PDS handles it itself.
    /// </summary>
    public static ScopedATProtoClient WithoutProxy(this IATProtoClient client)
        => client.WithRequestOptions(new XrpcRequestOptions { DisableProxy = true });

    /// <summary>
    /// Returns a client that sends <paramref name="labelerDids"/> in the
    /// <c>atproto-accept-labelers</c> header instead of the client's own list.
    /// </summary>
    public static ScopedATProtoClient WithAcceptLabelers(this IATProtoClient client, IEnumerable<string> labelerDids)
    {
        if (labelerDids == null)
        {
            throw new ArgumentNullException(nameof(labelerDids));
        }

        return client.WithRequestOptions(new XrpcRequestOptions { AcceptLabelers = labelerDids.ToArray() });
    }

    /// <summary>
    /// Returns a client that adds a header to every request.
    /// </summary>
    public static ScopedATProtoClient WithHeader(this IATProtoClient client, string name, string value)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Header name cannot be null or empty.", nameof(name));
        }

        return client.WithRequestOptions(new XrpcRequestOptions
        {
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = value },
        });
    }

    /// <summary>
    /// Returns a client that sends every request to another service. Session credentials are not
    /// sent to that service.
    /// </summary>
    public static ScopedATProtoClient WithServiceUrl(this IATProtoClient client, Uri serviceUrl)
    {
        if (serviceUrl == null)
        {
            throw new ArgumentNullException(nameof(serviceUrl));
        }

        return client.WithRequestOptions(new XrpcRequestOptions { ServiceUrl = serviceUrl });
    }

    #endregion

    private static XrpcRequestOptions? ProxyOptions(string? proxyServiceDid)
        => proxyServiceDid == null ? null : new XrpcRequestOptions { ProxyServiceDid = proxyServiceDid };

    private static IXrpcRequestClient AsXrpcClient(IATProtoClient client, string feature)
    {
        if (client is IXrpcRequestClient xrpc)
        {
            return xrpc;
        }

        throw NotSupported(client, feature);
    }

    private static NotSupportedException NotSupported(IATProtoClient client, string feature)
        => new NotSupportedException(
            $"{client.GetType().Name} does not implement {nameof(IXrpcRequestClient)}, which is required for {feature}.");
}
