using System.Text.Json.Serialization;
using CarpaNet.OAuth.Crypto;

namespace CarpaNet.OAuth;

/// <summary>
/// AOT-compatible JSON serialization context for OAuth types.
/// </summary>
[JsonSerializable(typeof(OAuthAuthorizationServerMetadata))]
[JsonSerializable(typeof(OAuthTokenResponse))]
[JsonSerializable(typeof(OAuthClientMetadata))]
[JsonSerializable(typeof(JsonWebKeySet))]
[JsonSerializable(typeof(JsonWebKey))]
[JsonSerializable(typeof(PushedAuthorizationResponse))]
[JsonSerializable(typeof(OAuthErrorResponse))]
[JsonSerializable(typeof(EcJwk))]
[JsonSerializable(typeof(DPoPProofHeader))]
[JsonSerializable(typeof(DPoPProofPayload))]
[JsonSerializable(typeof(JwtHeader))]
[JsonSerializable(typeof(ClientAssertionPayload))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class OAuthJsonContext : JsonSerializerContext
{
}
