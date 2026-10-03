using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet;
using CarpaNet.Auth;

namespace CarpaNet.Blob;

/// <summary>
/// Extension methods for blob operations on IATProtoClient.
/// </summary>
public static class ATProtoClientBlobExtensions
{
    private const string UploadBlobNsid = "com.atproto.repo.uploadBlob";
    private const string GetBlobNsid = "com.atproto.sync.getBlob";

    /// <summary>
    /// Uploads a blob to the user's PDS.
    /// </summary>
    /// <remarks>
    /// With a client that implements <see cref="IXrpcRequestClient"/> (including OAuth clients), the
    /// upload goes through the client's own authentication (Bearer or DPoP) and streams the content
    /// without buffering it. A seekable stream can be replayed after a token refresh. To report
    /// progress, wrap the stream in a <see cref="CarpaNet.Http.ProgressReportingStream"/>.
    /// Use <see cref="BlobRef.ToATBlob"/> to put the result in a generated record.
    /// </remarks>
    /// <param name="client">The ATProto client.</param>
    /// <param name="content">The blob content stream. It is read from its current position and not disposed.</param>
    /// <param name="mimeType">The MIME type of the blob.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reference to the uploaded blob.</returns>
    /// <exception cref="InvalidOperationException">If the client is not authenticated.</exception>
    public static async Task<BlobRef> UploadBlobAsync(
        this IATProtoClient client,
        Stream content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (!client.IsAuthenticated)
        {
            throw new InvalidOperationException("Blob upload requires authentication.");
        }

        if (client is IXrpcRequestClient xrpc)
        {
            var request = new XrpcRequest(HttpMethod.Post, UploadBlobNsid)
            {
                Body = XrpcBody.FromStream(content, mimeType),
            };

            using var response = await xrpc.SendXrpcAsync(request, cancellationToken).ConfigureAwait(false);
            return await ReadUploadResponseAsync(response, cancellationToken).ConfigureAwait(false);
        }

        // Get the token provider to access the access token
        var tokenProvider = client.TokenProvider
            ?? throw new InvalidOperationException("No token provider available.");

        var accessToken = await tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        return await UploadBlobInternalAsync(
            client,
            client.BaseUrl,
            content,
            mimeType,
            accessToken,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads a blob from a byte array.
    /// </summary>
    /// <param name="client">The ATProto client.</param>
    /// <param name="data">The blob data.</param>
    /// <param name="mimeType">The MIME type of the blob.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reference to the uploaded blob.</returns>
    public static async Task<BlobRef> UploadBlobAsync(
        this IATProtoClient client,
        byte[] data,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(data);
        return await client.UploadBlobAsync(stream, mimeType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads a blob from a file.
    /// </summary>
    /// <param name="client">The ATProto client.</param>
    /// <param name="filePath">Path to the file to upload.</param>
    /// <param name="mimeType">Optional MIME type. If not specified, will attempt to detect from extension.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reference to the uploaded blob.</returns>
    public static async Task<BlobRef> UploadBlobFromFileAsync(
        this IATProtoClient client,
        string filePath,
        string? mimeType = null,
        CancellationToken cancellationToken = default)
    {
        mimeType ??= GetMimeTypeFromExtension(Path.GetExtension(filePath));

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await client.UploadBlobAsync(stream, mimeType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads a blob from a PDS.
    /// </summary>
    /// <remarks>
    /// With a client that implements <see cref="IXrpcRequestClient"/>, a blob owned by another
    /// account is fetched from that account's PDS (found with the client's
    /// <see cref="IATProtoClient.IdentityResolver"/>) without session credentials; the user's own
    /// blobs are fetched from their PDS with the client's authentication.
    /// </remarks>
    /// <param name="client">The ATProto client.</param>
    /// <param name="did">The DID of the repo that owns the blob.</param>
    /// <param name="cid">The CID of the blob to download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The blob data as a byte array.</returns>
    public static async Task<byte[]> DownloadBlobAsync(
        this IATProtoClient client,
        ATDid did,
        ATCid cid,
        CancellationToken cancellationToken = default)
    {
        if (client is IXrpcRequestClient xrpc)
        {
            XrpcRequestOptions? options = null;
            var owner = did.ToString();
            if (!string.Equals(owner, client.AuthenticatedDid, StringComparison.Ordinal) && client.IdentityResolver != null)
            {
                var didDoc = await client.IdentityResolver.ResolveAsync(owner, cancellationToken).ConfigureAwait(false);
                if (didDoc.PdsEndpoint != null)
                {
                    options = new XrpcRequestOptions { ServiceUrl = new Uri(didDoc.PdsEndpoint) };
                }
            }

            var parameters = new[]
            {
                new KeyValuePair<string, string>("did", owner),
                new KeyValuePair<string, string>("cid", cid.ToString()),
            };

            return await xrpc.QueryBytesAsync(GetBlobNsid, parameters, options, cancellationToken).ConfigureAwait(false);
        }

        var url = new Uri(client.BaseUrl, $"/xrpc/com.atproto.sync.getBlob?did={did}&cid={cid}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (client.IsAuthenticated && client.TokenProvider != null)
        {
            var accessToken = await client.TokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        var response = await client.HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new ATProtoException(
                $"Blob download failed: {errorContent}",
                statusCode: response.StatusCode);
        }

#if NET8_0_OR_GREATER
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
    }

    private static async Task<BlobRef> UploadBlobInternalAsync(
        IATProtoClient client,
        Uri baseUrl,
        Stream content,
        string mimeType,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        var url = new Uri(baseUrl, "/xrpc/com.atproto.repo.uploadBlob");

        // Read stream to byte array for HttpContent
        byte[] data;
        if (content is MemoryStream ms && ms.TryGetBuffer(out var buffer))
        {
            data = buffer.Array != null ? buffer.ToArray() : Array.Empty<byte>();
        }
        else
        {
            using var memoryStream = new MemoryStream();
            await content.CopyToAsync(memoryStream).ConfigureAwait(false);
            data = memoryStream.ToArray();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new ByteArrayContent(data);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await client.HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadUploadResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<BlobRef> ReadUploadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new ATProtoException(
                $"Blob upload failed: {errorContent}",
                statusCode: response.StatusCode);
        }

#if NET8_0_OR_GREATER
        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var uploadResponse = await JsonSerializer.DeserializeAsync(
            responseStream,
            BlobJsonContext.Default.UploadBlobResponse,
            cancellationToken).ConfigureAwait(false);
#else
        var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var uploadResponse = JsonSerializer.Deserialize(responseContent, BlobJsonContext.Default.UploadBlobResponse);
#endif

        if (uploadResponse?.Blob == null)
        {
            throw new ATProtoException("Invalid blob upload response.");
        }

        return uploadResponse.Blob;
    }

    /// <summary>
    /// Gets a MIME type from a file extension.
    /// </summary>
    private static string GetMimeTypeFromExtension(string extension)
    {
        // TODO: Check the actual header bytes to determine MIME type instead of just relying on extension
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }
}
