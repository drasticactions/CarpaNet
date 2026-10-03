using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.Identity;

/// <summary>
/// Resolves handles to DIDs by calling <c>com.atproto.identity.resolveHandle</c> on an
/// ATProtocol service, such as a PDS or an AppView (for example <c>https://public.api.bsky.app</c>).
/// </summary>
/// <remarks>
/// <para>
/// This method works where DNS and <c>/.well-known/atproto-did</c> lookups do not, such as in
/// browsers, where UDP is unavailable and well-known requests are usually blocked by CORS.
/// </para>
/// <para>
/// <b>Trust:</b> the result is only as trustworthy as the service. The client does not check
/// the handle's DNS record or well-known file itself, so a malicious or out-of-date service can
/// return a wrong DID. <see cref="IdentityResolver.ResolveAsync(string, CancellationToken)"/> still
/// checks that the DID document claims the handle, but that check does not prove that the
/// handle's domain points back to the DID.
/// </para>
/// </remarks>
public sealed class XrpcHandleResolver
{
    private readonly HttpClient _httpClient;
    private readonly string _serviceUrl;

    /// <summary>
    /// The XRPC method used for handle resolution.
    /// </summary>
    public const string ResolveHandleMethod = "com.atproto.identity.resolveHandle";

    /// <summary>
    /// Creates a new XrpcHandleResolver.
    /// </summary>
    /// <param name="httpClient">The HttpClient used for requests. The resolver does not dispose it.</param>
    /// <param name="serviceUrl">The base URL of the service (for example <c>https://public.api.bsky.app</c>).</param>
    public XrpcHandleResolver(HttpClient httpClient, string serviceUrl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        if (string.IsNullOrWhiteSpace(serviceUrl))
            throw new ArgumentException("Service URL cannot be empty", nameof(serviceUrl));

        if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException($"Service URL must be an absolute HTTP(S) URL: {serviceUrl}", nameof(serviceUrl));

        _serviceUrl = serviceUrl.Trim().TrimEnd('/');
    }

    /// <summary>
    /// Gets the base URL of the service.
    /// </summary>
    public string ServiceUrl => _serviceUrl;

    /// <summary>
    /// Resolves a handle to a DID through the service.
    /// </summary>
    /// <param name="handle">The handle to resolve (e.g., "alice.bsky.social").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The DID, or null if the service returns an error status (for example 400 when the handle
    /// does not resolve) or a response that does not contain a valid DID.
    /// </returns>
    /// <exception cref="HttpRequestException">The request could not be sent.</exception>
    public async Task<string?> ResolveHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
            throw new ArgumentException("Handle cannot be empty", nameof(handle));

        var url = $"{_serviceUrl}/xrpc/{ResolveHandleMethod}?handle={Uri.EscapeDataString(handle)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        try
        {
#if NET5_0_OR_GREATER
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
            var result = await JsonSerializer.DeserializeAsync(stream, IdentityJsonContext.Default.ResolveHandleResponse, cancellationToken).ConfigureAwait(false);
            var did = result?.Did?.Trim();

            if (did != null && IdentityResolver.IsValidDid(did))
                return did;
        }
        catch (JsonException)
        {
            // Malformed response
        }
        catch (IOException)
        {
            // Truncated response
        }

        return null;
    }
}

/// <summary>
/// Output of <c>com.atproto.identity.resolveHandle</c>.
/// </summary>
internal sealed class ResolveHandleResponse
{
    /// <summary>
    /// The resolved DID.
    /// </summary>
    [JsonPropertyName("did")]
    public string? Did { get; set; }
}
