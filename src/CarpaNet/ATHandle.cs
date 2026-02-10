using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarpaNet;

/// <summary>
/// Represents a handle (username) in the ATProtocol.
/// Handles are domain names that identify accounts (e.g., "alice.bsky.social").
/// </summary>
[JsonConverter(typeof(ATHandleJsonConverter))]
public readonly struct ATHandle : IEquatable<ATHandle>
{
    /// <summary>
    /// The raw handle string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new ATHandle from a string value.
    /// </summary>
    public ATHandle(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Validates if this is a properly formatted handle.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(Value) &&
                           Value.Contains(".") &&
                           !Value.StartsWith(".") &&
                           !Value.EndsWith(".");

    public bool Equals(ATHandle other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is ATHandle other && Equals(other);
    public override int GetHashCode() => Value?.ToLowerInvariant().GetHashCode() ?? 0;
    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(ATHandle left, ATHandle right) => left.Equals(right);
    public static bool operator !=(ATHandle left, ATHandle right) => !left.Equals(right);

    public static implicit operator string(ATHandle handle) => handle.Value;
    public static implicit operator ATHandle(string value) => new(value);
}

public sealed class ATHandleJsonConverter : JsonConverter<ATHandle>
{
    public override ATHandle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return new ATHandle(value ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, ATHandle value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
