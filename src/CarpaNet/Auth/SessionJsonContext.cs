using System.Text.Json.Serialization;

namespace CarpaNet.Auth;

/// <summary>
/// JSON serialization context for session types (AOT-compatible).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(GetSessionResponse))]
internal partial class SessionJsonContext : JsonSerializerContext
{
}
