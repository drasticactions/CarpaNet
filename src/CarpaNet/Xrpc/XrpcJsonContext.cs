using System.Text.Json.Serialization;

namespace CarpaNet.Xrpc;

/// <summary>
/// JSON serialization context for XRPC types (AOT-compatible).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(XrpcError))]
internal partial class XrpcJsonContext : JsonSerializerContext
{
}
