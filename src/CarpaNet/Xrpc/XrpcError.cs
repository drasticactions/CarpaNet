using System.Text.Json.Serialization;

namespace CarpaNet.Xrpc;

/// <summary>
/// Represents an XRPC error response.
/// </summary>
public sealed class XrpcError
{
    /// <summary>
    /// Gets or sets the error code (e.g., "InvalidRequest", "ExpiredToken").
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the human-readable error message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Creates a formatted error message.
    /// </summary>
    /// <returns>A formatted error string.</returns>
    public string GetFormattedMessage()
    {
        if (!string.IsNullOrEmpty(Error) && !string.IsNullOrEmpty(Message))
        {
            return $"{Error}: {Message}";
        }

        return Message ?? Error ?? "Unknown error";
    }
}
