using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.OAuth;

namespace CarpaNet.Http;

/// <summary>
/// HTTP message handler that adds DPoP (Demonstrating Proof-of-Possession) authentication
/// to requests and automatically retries on 401 Unauthorized responses after refreshing the token.
/// Also manages DPoP nonce values from server responses.
/// </summary>
/// <remarks>
/// <para>
/// This handler can be used with <c>IHttpClientFactory</c> to add ATProtocol OAuth/DPoP
/// authentication to an HttpClient pipeline:
/// </para>
/// <code>
/// services.AddHttpClient("atproto-oauth")
///     .AddHttpMessageHandler(() => new ATProtoDPoPAuthHandler(dpopTokenProvider));
/// </code>
/// </remarks>
public sealed class ATProtoDPoPAuthHandler : DelegatingHandler
{
    private readonly DPoPTokenProvider _tokenProvider;

    /// <summary>
    /// Creates a new DPoP auth handler with the specified token provider.
    /// </summary>
    /// <param name="tokenProvider">The DPoP token provider for creating proofs and managing nonces.</param>
    public ATProtoDPoPAuthHandler(DPoPTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    /// <summary>
    /// Creates a new DPoP auth handler with the specified token provider and inner handler.
    /// </summary>
    /// <param name="tokenProvider">The DPoP token provider for creating proofs and managing nonces.</param>
    /// <param name="innerHandler">The inner handler.</param>
    public ATProtoDPoPAuthHandler(DPoPTokenProvider tokenProvider, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Add DPoP proof and auth headers
        _tokenProvider.AddDPoPHeaders(request);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _tokenProvider.UpdateNonceFromResponse(response, request.RequestUri!.ToString());

        // Retry on 401 with token refresh
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await _tokenProvider.RefreshAsync(cancellationToken).ConfigureAwait(false);

            // Clone request and add fresh DPoP headers
            using var retryRequest = ATProtoClientCore.CloneRequest(request, "Authorization", "DPoP");
            _tokenProvider.AddDPoPHeaders(retryRequest);

            // Copy custom headers (e.g., atproto-proxy)
            foreach (var header in request.Headers)
            {
                if (header.Key != "Authorization" && header.Key != "DPoP")
                {
                    retryRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            response.Dispose();
            response = await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
            _tokenProvider.UpdateNonceFromResponse(response, retryRequest.RequestUri!.ToString());
        }

        return response;
    }
}
