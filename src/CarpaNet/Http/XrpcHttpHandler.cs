using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
#if NET8_0_OR_GREATER
using System.Text.Json.Serialization.Metadata;
#endif
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Xrpc;

namespace CarpaNet.Http;

/// <summary>
/// Provides shared HTTP handling logic for XRPC requests.
/// Handles URL building, response processing, and error handling.
/// </summary>
internal static class XrpcHttpHandler
{
    private const string XrpcPathPrefix = "/xrpc/";
    private const string ContentTypeJson = "application/json";

    /// <summary>
    /// Builds an XRPC URL with query parameters.
    /// </summary>
    /// <param name="baseUrl">The base URL of the service.</param>
    /// <param name="nsid">The NSID of the endpoint.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <returns>The complete URL.</returns>
    public static Uri BuildUrl(Uri baseUrl, string nsid, IReadOnlyDictionary<string, string>? parameters = null)
    {
        if (string.IsNullOrEmpty(nsid))
            throw new ArgumentException("NSID cannot be null or empty.", nameof(nsid));

        var uriBuilder = new UriBuilder(baseUrl)
        {
            Path = XrpcPathPrefix + nsid
        };

        if (parameters != null && parameters.Count > 0)
        {
            var queryBuilder = new StringBuilder();
            var first = true;

            foreach (var kvp in parameters)
            {
                if (string.IsNullOrEmpty(kvp.Value))
                    continue;

                if (!first)
                    queryBuilder.Append('&');

                queryBuilder.Append(Uri.EscapeDataString(kvp.Key));
                queryBuilder.Append('=');
                queryBuilder.Append(Uri.EscapeDataString(kvp.Value));
                first = false;
            }

            if (queryBuilder.Length > 0)
            {
                uriBuilder.Query = queryBuilder.ToString();
            }
        }

        return uriBuilder.Uri;
    }

    /// <summary>
    /// Builds a WebSocket URL for subscriptions.
    /// </summary>
    /// <param name="baseUrl">The base URL of the service.</param>
    /// <param name="nsid">The NSID of the subscription endpoint.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <returns>The WebSocket URL.</returns>
    public static Uri BuildWebSocketUrl(Uri baseUrl, string nsid, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var url = BuildUrl(baseUrl, nsid, parameters);
        var uriBuilder = new UriBuilder(url);

        // Convert HTTP scheme to WebSocket scheme
        uriBuilder.Scheme = uriBuilder.Scheme switch
        {
            "http" => "ws",
            "https" => "wss",
            _ => uriBuilder.Scheme
        };

        return uriBuilder.Uri;
    }

    /// <summary>
    /// Creates an HTTP request message for a GET request.
    /// </summary>
    public static HttpRequestMessage CreateGetRequest(
        Uri url,
        string? proxyServiceDid = null,
        IEnumerable<string>? labelerDids = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddCommonHeaders(request, proxyServiceDid, labelerDids);
        return request;
    }

    /// <summary>
    /// Creates an HTTP request message for a POST request with JSON body.
    /// </summary>
    public static HttpRequestMessage CreatePostRequest<TInput>(
        Uri url,
        TInput? input,
        JsonSerializerOptions jsonOptions,
        string? proxyServiceDid = null,
        IEnumerable<string>? labelerDids = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        AddCommonHeaders(request, proxyServiceDid, labelerDids);

        if (input != null)
        {
#if NET8_0_OR_GREATER
            var typeInfo = (JsonTypeInfo<TInput>)jsonOptions.GetTypeInfo(typeof(TInput));
            var json = JsonSerializer.Serialize(input, typeInfo);
#else
            var json = JsonSerializer.Serialize(input, jsonOptions);
#endif
            request.Content = new StringContent(json, Encoding.UTF8, ContentTypeJson);
        }

        return request;
    }

    /// <summary>
    /// Adds common XRPC headers to a request.
    /// </summary>
    private static void AddCommonHeaders(
        HttpRequestMessage request,
        string? proxyServiceDid,
        IEnumerable<string>? labelerDids)
    {
        if (!string.IsNullOrEmpty(proxyServiceDid))
        {
            request.Headers.TryAddWithoutValidation("atproto-proxy", proxyServiceDid);
        }

        if (labelerDids != null)
        {
            var labelers = string.Join(",", labelerDids.Where(d => !string.IsNullOrEmpty(d)));
            if (!string.IsNullOrEmpty(labelers))
            {
                request.Headers.TryAddWithoutValidation("atproto-accept-labelers", labelers);
            }
        }
    }

    /// <summary>
    /// Processes an HTTP response and deserializes the result.
    /// Throws appropriate exceptions for error responses.
    /// </summary>
    public static async Task<TOutput> ProcessResponseAsync<TOutput>(
        HttpResponseMessage response,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken = default)
    {
        // Check for errors first
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForErrorResponseAsync(response, cancellationToken).ConfigureAwait(false);
        }

        // Handle 204 No Content
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            // If TOutput is nullable or a reference type, return default
            return default!;
        }

        // Deserialize successful response
#if NET8_0_OR_GREATER
        var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var typeInfo = (JsonTypeInfo<TOutput>)jsonOptions.GetTypeInfo(typeof(TOutput));
        var result = await JsonSerializer.DeserializeAsync(content, typeInfo, cancellationToken).ConfigureAwait(false);
#else
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<TOutput>(content, jsonOptions);
#endif

        if (result == null)
        {
            throw new ATProtoException(
                "Failed to deserialize response.",
                errorCode: "DeserializationError",
                statusCode: response.StatusCode);
        }

        return result;
    }

    /// <summary>
    /// Throws an appropriate exception for an error response.
    /// </summary>
    public static async Task ThrowForErrorResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        XrpcError? error = null;

        // Try to parse error response body
        try
        {
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType == ContentTypeJson)
            {
#if NET8_0_OR_GREATER
                var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                error = await JsonSerializer.DeserializeAsync(content, XrpcJsonContext.Default.XrpcError, cancellationToken).ConfigureAwait(false);
#else
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                error = JsonSerializer.Deserialize(content, XrpcJsonContext.Default.XrpcError);
#endif
            }
        }
        catch
        {
            // Ignore deserialization errors - will use status code for message
        }

        var message = error?.GetFormattedMessage() ?? GetDefaultErrorMessage(response.StatusCode);
        var errorCode = error?.Error;

        // Throw appropriate exception type based on status code
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                throw new AuthenticationException(message, errorCode);

            case (HttpStatusCode)429: // TooManyRequests
                var rateLimitInfo = RateLimitInfo.FromHeaders(name =>
                    response.Headers.TryGetValues(name, out var values)
                        ? values.FirstOrDefault()
                        : null);
                throw new RateLimitException(message, rateLimitInfo, errorCode);

            case HttpStatusCode.BadRequest:
                throw new ValidationException(message, errorCode);

            default:
                throw new ATProtoException(message, errorCode, response.StatusCode);
        }
    }

    /// <summary>
    /// Gets a default error message for an HTTP status code.
    /// </summary>
    private static string GetDefaultErrorMessage(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest => "Bad request",
            HttpStatusCode.Unauthorized => "Authentication required",
            HttpStatusCode.Forbidden => "Access denied",
            HttpStatusCode.NotFound => "Resource not found",
            HttpStatusCode.RequestEntityTooLarge => "Payload too large",
            (HttpStatusCode)429 => "Rate limit exceeded", // TooManyRequests
            HttpStatusCode.InternalServerError => "Internal server error",
            HttpStatusCode.BadGateway => "Bad gateway",
            HttpStatusCode.ServiceUnavailable => "Service unavailable",
            HttpStatusCode.GatewayTimeout => "Gateway timeout",
            _ => $"Request failed with status code {(int)statusCode}"
        };
    }
}
