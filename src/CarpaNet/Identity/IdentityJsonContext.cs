using System.Text.Json.Serialization;

namespace CarpaNet.Identity;

/// <summary>
/// JSON serialization context for DNS-over-HTTPS and XRPC handle resolution responses.
/// </summary>
[JsonSerializable(typeof(DnsJsonResponse))]
[JsonSerializable(typeof(ResolveHandleResponse))]
internal partial class IdentityJsonContext : JsonSerializerContext
{
}
