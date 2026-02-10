using System.Text.Json.Serialization;

namespace CarpaNet.Blob;

/// <summary>
/// JSON serialization context for blob types.
/// </summary>
[JsonSerializable(typeof(BlobRef))]
[JsonSerializable(typeof(BlobLink))]
[JsonSerializable(typeof(UploadBlobResponse))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class BlobJsonContext : JsonSerializerContext
{
}
