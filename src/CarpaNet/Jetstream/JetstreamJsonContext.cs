using System.Text.Json.Serialization;

namespace CarpaNet.Jetstream;

/// <summary>
/// JSON serialization context for Jetstream types (AOT-compatible).
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JetstreamEvent))]
[JsonSerializable(typeof(JetstreamOptionsUpdate))]
internal partial class JetstreamJsonContext : JsonSerializerContext
{
}
