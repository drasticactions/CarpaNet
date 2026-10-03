using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Auth;
using CarpaNet.Identity;

namespace CarpaNet;

/// <summary>
/// A view of another client that applies <see cref="XrpcRequestOptions"/> to every request.
/// Created with <see cref="ATProtoClientXrpcExtensions.WithRequestOptions(IATProtoClient, XrpcRequestOptions)"/>.
/// </summary>
/// <remarks>
/// The scoped client shares the inner client's session, token provider and <see cref="System.Net.Http.HttpClient"/>.
/// Disposing the inner client makes the scoped client unusable; the scoped client itself owns nothing.
/// </remarks>
public sealed class ScopedATProtoClient : IATProtoClient, IXrpcRequestClient
{
    private readonly IXrpcRequestClient _xrpc;

    /// <summary>
    /// Creates a scoped client.
    /// </summary>
    /// <param name="inner">The client to send through. It must implement <see cref="IXrpcRequestClient"/>.</param>
    /// <param name="options">The options to apply to every request.</param>
    public ScopedATProtoClient(IATProtoClient inner, XrpcRequestOptions options)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _xrpc = inner as IXrpcRequestClient
            ?? throw new ArgumentException(
                $"{inner.GetType().Name} does not implement {nameof(IXrpcRequestClient)}.", nameof(inner));
    }

    /// <summary>
    /// Gets the client requests are sent through.
    /// </summary>
    public IATProtoClient Inner { get; }

    /// <summary>
    /// Gets the options applied to every request.
    /// </summary>
    public XrpcRequestOptions Options { get; }

    /// <inheritdoc/>
    public Uri BaseUrl => Options.ServiceUrl ?? Inner.BaseUrl;

    /// <inheritdoc/>
    public bool IsAuthenticated => Inner.IsAuthenticated;

    /// <inheritdoc/>
    public string? AuthenticatedDid => Inner.AuthenticatedDid;

    /// <inheritdoc/>
    public IdentityResolver? IdentityResolver => Inner.IdentityResolver;

    /// <inheritdoc/>
    public ITokenProvider? TokenProvider => Inner.TokenProvider;

    /// <inheritdoc/>
    public HttpClient HttpClient => Inner.HttpClient;

    /// <inheritdoc/>
    public IReadOnlyList<string>? LabelerDids => Options.AcceptLabelers ?? Inner.LabelerDids;

    /// <inheritdoc/>
    public JsonSerializerOptions JsonOptions => _xrpc.JsonOptions;

    /// <inheritdoc/>
    public Task<HttpResponseMessage> SendXrpcAsync(XrpcRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        request.Options = XrpcRequestOptions.Combine(Options, request.Options);
        return _xrpc.SendXrpcAsync(request, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TOutput> GetAsync<TOutput>(
        string nsid,
        IEnumerable<KeyValuePair<string, string>>? parameters = null,
        CancellationToken cancellationToken = default)
        => this.QueryAsync<TOutput>(nsid, parameters, null, cancellationToken);

    /// <inheritdoc/>
    public Task<TOutput> GetAsync<TOutput>(
        string nsid,
        string proxyServiceDid,
        IEnumerable<KeyValuePair<string, string>>? parameters = null,
        CancellationToken cancellationToken = default)
        => this.QueryAsync<TOutput>(nsid, parameters, new XrpcRequestOptions { ProxyServiceDid = proxyServiceDid }, cancellationToken);

    /// <inheritdoc/>
    public Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        TInput? input,
        CancellationToken cancellationToken = default)
        => this.ProcedureAsync<TInput, TOutput>(nsid, null, input, null, cancellationToken);

    /// <inheritdoc/>
    public Task<TOutput> PostAsync<TInput, TOutput>(
        string nsid,
        string proxyServiceDid,
        TInput? input,
        CancellationToken cancellationToken = default)
        => this.ProcedureAsync<TInput, TOutput>(nsid, null, input, new XrpcRequestOptions { ProxyServiceDid = proxyServiceDid }, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<TMessage> SubscribeAsync<TMessage>(
        string nsid,
        IEnumerable<KeyValuePair<string, string>>? parameters = null,
        CancellationToken cancellationToken = default)
        => Inner.SubscribeAsync<TMessage>(nsid, parameters, cancellationToken);
}
