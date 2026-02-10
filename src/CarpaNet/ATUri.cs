using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarpaNet;

/// <summary>
/// Represents an AT URI in the format "at://did/collection/rkey".
/// </summary>
[JsonConverter(typeof(ATUriJsonConverter))]
public readonly struct ATUri : IEquatable<ATUri>
{
    /// <summary>
    /// The raw AT URI string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new ATUri from a string value.
    /// </summary>
    public ATUri(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the authority (DID or handle) from the URI.
    /// </summary>
    public string? Authority
    {
        get
        {
            if (string.IsNullOrEmpty(Value) || !Value.StartsWith("at://"))
            {
                return null;
            }

            var withoutScheme = Value.Substring(5); // "at://".Length
            var slashIndex = withoutScheme.IndexOf('/');
            return slashIndex >= 0 ? withoutScheme.Substring(0, slashIndex) : withoutScheme;
        }
    }

    /// <summary>
    /// Gets the collection NSID from the URI.
    /// </summary>
    public string? Collection
    {
        get
        {
            if (string.IsNullOrEmpty(Value) || !Value.StartsWith("at://"))
            {
                return null;
            }

            var withoutScheme = Value.Substring(5);
            var parts = withoutScheme.Split('/');
            return parts.Length >= 2 ? parts[1] : null;
        }
    }

    /// <summary>
    /// Gets the record key from the URI.
    /// </summary>
    public string? RecordKey
    {
        get
        {
            if (string.IsNullOrEmpty(Value) || !Value.StartsWith("at://"))
            {
                return null;
            }

            var withoutScheme = Value.Substring(5);
            var parts = withoutScheme.Split('/');
            return parts.Length >= 3 ? parts[2] : null;
        }
    }

    /// <summary>
    /// Validates if this is a properly formatted AT URI.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(Value) && Value.StartsWith("at://");

    /// <summary>
    /// Returns true if the authority is a DID (starts with "did:").
    /// </summary>
    public bool AuthorityIsDid
    {
        get
        {
            var auth = Authority;
            return auth != null && auth.StartsWith("did:", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Returns true if the authority is a handle (not a DID).
    /// </summary>
    public bool AuthorityIsHandle
    {
        get
        {
            var auth = Authority;
            return auth != null && !auth.StartsWith("did:", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Gets the authority as an ATDid if it's a DID, otherwise null.
    /// </summary>
    public ATDid? AuthorityAsDid
    {
        get
        {
            var auth = Authority;
            if (auth != null && auth.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
            {
                return new ATDid(auth);
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the authority as an ATHandle if it's a handle, otherwise null.
    /// </summary>
    public ATHandle? AuthorityAsHandle
    {
        get
        {
            var auth = Authority;
            if (auth != null && !auth.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
            {
                return new ATHandle(auth);
            }
            return null;
        }
    }

    /// <summary>
    /// Creates an AT URI from components.
    /// </summary>
    public static ATUri Create(string authority, string? collection = null, string? recordKey = null)
    {
        var uri = $"at://{authority}";
        if (!string.IsNullOrEmpty(collection))
        {
            uri += $"/{collection}";
            if (!string.IsNullOrEmpty(recordKey))
            {
                uri += $"/{recordKey}";
            }
        }
        return new ATUri(uri);
    }

    public bool Equals(ATUri other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ATUri other && Equals(other);
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;
    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(ATUri left, ATUri right) => left.Equals(right);
    public static bool operator !=(ATUri left, ATUri right) => !left.Equals(right);

    public static implicit operator string(ATUri uri) => uri.Value;
    public static implicit operator ATUri(string value) => new(value);
}

public sealed class ATUriJsonConverter : JsonConverter<ATUri>
{
    public override ATUri Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return new ATUri(value ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, ATUri value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
