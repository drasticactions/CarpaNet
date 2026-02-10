using System;
using System.Text.Json.Serialization;

namespace CarpaNet.Blob;

/// <summary>
/// A reference to an uploaded blob.
/// </summary>
public sealed class BlobRef
{
    /// <summary>
    /// Gets or sets the type discriminator.
    /// </summary>
    [JsonPropertyName("$type")]
    public string Type { get; set; } = "blob";

    /// <summary>
    /// Gets or sets the blob reference.
    /// </summary>
    [JsonPropertyName("ref")]
    public BlobLink? Ref { get; set; }

    /// <summary>
    /// Gets or sets the MIME type of the blob.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the size of the blob in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>
/// A CID link to a blob.
/// </summary>
public sealed class BlobLink
{
    /// <summary>
    /// Gets or sets the CID of the blob.
    /// </summary>
    [JsonPropertyName("$link")]
    public string? Link { get; set; }
}

/// <summary>
/// Response from blob upload.
/// </summary>
public sealed class UploadBlobResponse
{
    /// <summary>
    /// Gets or sets the blob reference.
    /// </summary>
    [JsonPropertyName("blob")]
    public BlobRef? Blob { get; set; }
}
