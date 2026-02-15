using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Auth;

namespace CarpaNet.Http;

/// <summary>
/// HTTP message handler that adds Bearer token authentication to requests
/// and automatically retries on 401 Unauthorized responses after refreshing the token.
/// </summary>
/// <remarks>
/// <para>
/// This handler can be used with <c>IHttpClientFactory</c> to add ATProtocol authentication
/// to an HttpClient pipeline:
/// </para>
/// <code>
/// services.AddHttpClient("atproto")
///     .AddHttpMessageHandler(() => new ATProtoBearerAuthHandler(tokenProvider));
/// </code>
/// </remarks>
public sealed class ATProtoBearerAuthHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;

    /// <summary>
    /// Creates a new Bearer auth handler with the specified token provider.
    /// </summary>
    /// <param name="tokenProvider">The token provider for obtaining access tokens.</param>
    public ATProtoBearerAuthHandler(ITokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    /// <summary>
    /// Creates a new Bearer auth handler with the specified token provider and inner handler.
    /// </summary>
    /// <param name="tokenProvider">The token provider for obtaining access tokens.</param>
    /// <param name="innerHandler">The inner handler.</param>
    public ATProtoBearerAuthHandler(ITokenProvider tokenProvider, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Add auth header
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Retry on 401 with token refresh
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            try
            {
                await _tokenProvider.RefreshAsync(cancellationToken).ConfigureAwait(false);

                using var retryRequest = XrpcHttpHandler.CloneRequest(request, "Authorization");
                token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                {
                    retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                response.Dispose();
                return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (AuthenticationException)
            {
                // Refresh failed, return original 401 response
            }
            catch (InvalidOperationException)
            {
                // No refresh token available
            }
        }

        return response;
    }
}
