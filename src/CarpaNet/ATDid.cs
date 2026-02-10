using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarpaNet;

/// <summary>
/// Represents a Decentralized Identifier (DID) in the ATProtocol.
/// DIDs always start with "did:" followed by a method and method-specific identifier.
/// </summary>
[JsonConverter(typeof(ATDidJsonConverter))]
public readonly struct ATDid : IEquatable<ATDid>
{
    /// <summary>
    /// The raw DID string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new ATDid from a string value.
    /// </summary>
    public ATDid(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the DID method (e.g., "plc", "web").
    /// </summary>
    public string? Method
    {
        get
        {
            if (string.IsNullOrEmpty(Value) || !Value.StartsWith("did:"))
            {
                return null;
            }

            var parts = Value.Split(':');
            return parts.Length >= 2 ? parts[1] : null;
        }
    }

    /// <summary>
    /// Validates if this is a properly formatted DID.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(Value) && Value.StartsWith("did:");

    public bool Equals(ATDid other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ATDid other && Equals(other);
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;
    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(ATDid left, ATDid right) => left.Equals(right);
    public static bool operator !=(ATDid left, ATDid right) => !left.Equals(right);

    public static implicit operator string(ATDid did) => did.Value;
    public static implicit operator ATDid(string value) => new(value);
}

public sealed class ATDidJsonConverter : JsonConverter<ATDid>
{
    public override ATDid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return new ATDid(value ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, ATDid value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
