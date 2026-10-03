using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet;

/// <summary>
/// The low-level XRPC send operation shared by the client implementations.
/// </summary>
/// <remarks>
/// <see cref="ATProtoClient"/>, the OAuth client and <see cref="ScopedATProtoClient"/> implement this
/// interface. The extension methods in <see cref="ATProtoClientXrpcExtensions"/> use it for binary
/// bodies, binary responses, procedures with query parameters and per-request options.
/// </remarks>
public interface IXrpcRequestClient
{
    /// <summary>
    /// Gets the JSON options used to serialize request bodies and deserialize responses.
    /// </summary>
    JsonSerializerOptions JsonOptions { get; }

    /// <summary>
    /// Sends an XRPC request through the client's authentication pipeline and returns the raw response.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The HTTP response, which the caller disposes. Error statuses are not thrown.</returns>
    Task<HttpResponseMessage> SendXrpcAsync(XrpcRequest request, CancellationToken cancellationToken = default);
}
