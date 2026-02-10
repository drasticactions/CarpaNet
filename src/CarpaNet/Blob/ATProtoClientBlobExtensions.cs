using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet.Auth;

namespace CarpaNet.Blob;

/// <summary>
/// Extension methods for blob operations on ATProtoClient.
/// </summary>
public static class ATProtoClientBlobExtensions
{
    /// <summary>
    /// Uploads a blob to the user's PDS.
    /// </summary>
    /// <param name="client">The ATProto client.</param>
    /// <param name="content">The blob content stream.</param>
    /// <param name="mimeType">The MIME type of the blob.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reference to the uploaded blob.</returns>
    /// <exception cref="InvalidOperationException">If the client is not authenticated.</exception>
    public static async Task<BlobRef> UploadBlobAsync(
        this ATProtoClient client,
        Stream content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (!client.IsAuthenticated)
        {
            throw new InvalidOperationException("Blob upload requires authentication.");
        }

        // Get the token provider to access the access token
        var tokenProvider = client.TokenProvider
            ?? throw new InvalidOperationException("No token provider available.");

        var accessToken = await tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        return await UploadBlobInternalAsync(
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
        this ATProtoClient client,
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
        this ATProtoClient client,
        string filePath,
        string? mimeType = null,
        CancellationToken cancellationToken = default)
    {
        mimeType ??= GetMimeTypeFromExtension(Path.GetExtension(filePath));

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await client.UploadBlobAsync(stream, mimeType, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<BlobRef> UploadBlobInternalAsync(
        Uri baseUrl,
        Stream content,
        string mimeType,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        var url = new Uri(baseUrl, "/xrpc/com.atproto.repo.uploadBlob");

        using var httpClient = new HttpClient();

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

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

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
