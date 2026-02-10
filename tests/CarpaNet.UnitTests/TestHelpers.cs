using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CarpaNet.Cbor;

namespace CarpaNet.UnitTests;

/// <summary>
/// Provides pre-configured JSON and CBOR options for unit tests.
/// </summary>
internal static class TestHelpers
{
    public static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
    }

    public static CborSerializerContext CreateCborContext()
    {
        return CborSerializerContext.Default;
    }
}
