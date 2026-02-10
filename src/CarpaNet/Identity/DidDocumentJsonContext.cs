using System.Text.Json.Serialization;

namespace CarpaNet.Identity;

/// <summary>
/// JSON serialization context for DID document types.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DidDocument))]
[JsonSerializable(typeof(VerificationMethod))]
[JsonSerializable(typeof(DidService))]
internal partial class DidDocumentJsonContext : JsonSerializerContext
{
}
