using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarpaNet;

/// <summary>
/// Represents an AT identifier which can be either a DID or a Handle.
/// </summary>
[JsonConverter(typeof(ATIdentifierJsonConverter))]
public readonly struct ATIdentifier : IEquatable<ATIdentifier>
{
    /// <summary>
    /// The raw identifier string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new ATIdentifier from a string value.
    /// </summary>
    public ATIdentifier(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Returns true if this identifier is a DID.
    /// </summary>
    public bool IsDid => DIDValidator.EnsureValidDid(Value);

    /// <summary>
    /// Returns true if this identifier is a Handle.
    /// </summary>
    public bool IsHandle => HandleValidator.EnsureValidHandle(Value);

    /// <summary>
    /// Returns true if this identifier is either a valid DID or a valid Handle.
    /// </summary>
    public bool IsValid => IsDid || IsHandle;

    /// <summary>
    /// Gets this identifier as a DID, if it is one.
    /// </summary>
    public ATDid? AsDid => IsDid ? new ATDid(Value) : (ATDid?)null;

    /// <summary>
    /// Gets this identifier as a Handle, if it is one.
    /// </summary>
    public ATHandle? AsHandle => IsHandle ? new ATHandle(Value) : (ATHandle?)null;

    public bool Equals(ATIdentifier other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is ATIdentifier other && Equals(other);
    public override int GetHashCode() => Value?.ToLowerInvariant().GetHashCode() ?? 0;
    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(ATIdentifier left, ATIdentifier right) => left.Equals(right);
    public static bool operator !=(ATIdentifier left, ATIdentifier right) => !left.Equals(right);

    public static implicit operator string(ATIdentifier identifier) => identifier.Value;
    public static implicit operator ATIdentifier(string value) => new(value);
    public static implicit operator ATIdentifier(ATDid did) => new(did.Value);
    public static implicit operator ATIdentifier(ATHandle handle) => new(handle.Value);
}

public sealed class ATIdentifierJsonConverter : JsonConverter<ATIdentifier>
{
    public override ATIdentifier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return new ATIdentifier(value ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, ATIdentifier value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
