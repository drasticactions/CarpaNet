using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Cbor;
using CarpaNet.EventStream;

namespace CarpaNet.Http;

/// <summary>
/// Internal helper that provides shared XRPC operations used by all IATProtoClient implementations.
/// Handles URL building, request creation, response processing, subscribe flow, and request cloning.
/// Auth injection and send-with-retry logic remain in each client.
/// </summary>
internal sealed class ATProtoClientCore
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CborSerializerContext _cborContext;

    public HttpClient HttpClient => _httpClient;
    public JsonSerializerOptions JsonOptions => _jsonOptions;

    public ATProtoClientCore(HttpClient httpClient, JsonSerializerOptions jsonOptions, CborSerializerContext cborContext)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        _cborContext = cborContext ?? throw new ArgumentNullException(nameof(cborContext));
    }

    /// <summary>
    /// Builds an XRPC URL.
    /// </summary>
    public Uri BuildUrl(Uri baseUrl, string nsid, IReadOnlyDictionary<string, string>? parameters = null)
        => XrpcHttpHandler.BuildUrl(baseUrl, nsid, parameters);

    /// <summary>
    /// Creates an HTTP GET request with common XRPC headers.
    /// </summary>
    public HttpRequestMessage CreateGetRequest(Uri url, string? proxyServiceDid, IReadOnlyList<string>? labelerDids)
        => XrpcHttpHandler.CreateGetRequest(url, proxyServiceDid, labelerDids);

    /// <summary>
    /// Creates an HTTP POST request with JSON body and common XRPC headers.
    /// </summary>
    public HttpRequestMessage CreatePostRequest<TInput>(Uri url, TInput? input, string? proxyServiceDid, IReadOnlyList<string>? labelerDids)
        => XrpcHttpHandler.CreatePostRequest(url, input, _jsonOptions, proxyServiceDid, labelerDids);

    /// <summary>
    /// Processes an XRPC response and deserializes the result.
    /// </summary>
    public Task<TOutput> ProcessResponseAsync<TOutput>(HttpResponseMessage response, CancellationToken cancellationToken)
        => XrpcHttpHandler.ProcessResponseAsync<TOutput>(response, _jsonOptions, cancellationToken);

    /// <summary>
    /// Subscribes to an event stream via WebSocket.
    /// </summary>
    public async IAsyncEnumerable<TMessage> SubscribeAsync<TMessage>(
        Uri baseUrl,
        string nsid,
        IReadOnlyDictionary<string, string>? parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var eventStreamClient = new EventStreamClient(baseUrl, _cborContext);
        try
        {
            var paramList = parameters != null
                ? new List<KeyValuePair<string, string?>>(
                    ((IEnumerable<KeyValuePair<string, string>>)parameters)
                        .Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value)))
                : null;

            await foreach (var message in eventStreamClient.SubscribeAsync<TMessage>(nsid, paramList, cancellationToken).ConfigureAwait(false))
            {
                yield return message;
            }
        }
        finally
        {
            eventStreamClient.Dispose();
        }
    }

    /// <summary>
    /// Clones an HTTP request message, optionally skipping specified headers.
    /// Used for 401 retry flows where the original request cannot be reused.
    /// </summary>
    public static HttpRequestMessage CloneRequest(HttpRequestMessage original, params string[] skipHeaders)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        foreach (var header in original.Headers)
        {
            var shouldSkip = false;
            for (var i = 0; i < skipHeaders.Length; i++)
            {
                if (header.Key.Equals(skipHeaders[i], StringComparison.OrdinalIgnoreCase))
                {
                    shouldSkip = true;
                    break;
                }
            }

            if (!shouldSkip)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (original.Content != null)
        {
            var contentBytes = original.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            clone.Content = new ByteArrayContent(contentBytes);

            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
