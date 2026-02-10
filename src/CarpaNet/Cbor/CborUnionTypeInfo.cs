using System;
using System.Collections.Generic;
using System.Formats.Cbor;

namespace CarpaNet.Cbor;

/// <summary>
/// Base class for union (polymorphic) CBOR type info.
/// Handles deserialization of types that have a $type discriminator field.
/// </summary>
/// <typeparam name="TInterface">The interface type that all union variants implement.</typeparam>
public abstract class CborUnionTypeInfo<TInterface> : CborTypeInfo<TInterface>
{
    /// <summary>
    /// Dictionary mapping $type discriminator values to concrete type infos.
    /// </summary>
    protected abstract IReadOnlyDictionary<string, ICborTypeInfo> DerivedTypes { get; }

    /// <inheritdoc/>
    public override TInterface CreateInstance()
        => throw new NotSupportedException("Cannot instantiate union interface directly. Use a concrete type.");

    /// <inheritdoc/>
    public override TInterface? Read(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return default;
        }

        if (state != CborReaderState.StartMap)
        {
            throw new InvalidOperationException($"Expected map for union type, got {state}");
        }

        // Save position for re-reading
        var data = reader.GetRemainingData();

        // First pass: find $type discriminator
        string? discriminator = PeekTypeDiscriminator(ref reader);

        // Restore reader position
        reader = new DagCborReader(data);

        if (discriminator != null && DerivedTypes.TryGetValue(discriminator, out var typeInfo))
        {
            return (TInterface?)typeInfo.ReadObject(ref reader);
        }

        // If no discriminator found or unknown type, try to read as first available type
        // This handles cases where $type might be optional
        if (discriminator == null && DerivedTypes.Count > 0)
        {
            // Restore reader again since PeekTypeDiscriminator consumed some data
            reader = new DagCborReader(data);

            // Try each derived type until one succeeds
            foreach (var kvp in DerivedTypes)
            {
                try
                {
                    var tempReader = new DagCborReader(data);
                    var result = (TInterface?)kvp.Value.ReadObject(ref tempReader);
                    // If successful, advance the original reader
                    reader = tempReader;
                    return result;
                }
                catch
                {
                    // Try next type
                }
            }
        }

        throw new InvalidOperationException($"Unknown or missing type discriminator for union: {discriminator ?? "(none)"}. Expected one of: {string.Join(", ", DerivedTypes.Keys)}");
    }

    /// <inheritdoc/>
    public override void Write(ref DagCborWriter writer, TInterface? value)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        // Find matching type info by runtime type
        var runtimeType = value.GetType();
        foreach (var kvp in DerivedTypes)
        {
            if (kvp.Value.TargetType == runtimeType)
            {
                kvp.Value.WriteObject(ref writer, value);
                return;
            }
        }

        throw new InvalidOperationException($"No type info registered for runtime type: {runtimeType.FullName}. Available types: {string.Join(", ", GetRegisteredTypeNames())}");
    }

    /// <summary>
    /// Peeks into a CBOR map to find the $type discriminator value without consuming the entire map.
    /// </summary>
    private static string? PeekTypeDiscriminator(ref DagCborReader reader)
    {
        var count = reader.ReadStartMap();
        var remaining = count ?? int.MaxValue;

        while (remaining > 0 && reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();
            if (key == "$type")
            {
                return reader.ReadTextString();
            }
            reader.SkipValue();
            remaining--;
        }

        return null;
    }

    /// <summary>
    /// Gets the registered type names for error messages.
    /// </summary>
    private IEnumerable<string> GetRegisteredTypeNames()
    {
        foreach (var kvp in DerivedTypes)
        {
            yield return $"{kvp.Key} -> {kvp.Value.TargetType.Name}";
        }
    }
}

/// <summary>
/// Base class for open union types that can contain unknown types.
/// Open unions include an "unknown" variant that captures unrecognized $type values.
/// </summary>
/// <typeparam name="TInterface">The interface type that all union variants implement.</typeparam>
public abstract class CborOpenUnionTypeInfo<TInterface> : CborUnionTypeInfo<TInterface>
{
    /// <summary>
    /// Creates an instance to hold an unknown union variant.
    /// The returned instance should store the discriminator and raw CBOR data.
    /// </summary>
    /// <param name="discriminator">The $type value that was not recognized.</param>
    /// <param name="rawData">The raw CBOR data of the unknown type.</param>
    /// <returns>An instance representing the unknown type.</returns>
    protected abstract TInterface CreateUnknownInstance(string discriminator, byte[] rawData);

    /// <inheritdoc/>
    public override TInterface? Read(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == CborReaderState.Null)
        {
            reader.ReadNull();
            return default;
        }

        if (state != CborReaderState.StartMap)
        {
            throw new InvalidOperationException($"Expected map for union type, got {state}");
        }

        // Save position for re-reading
        var data = reader.GetRemainingData();

        // First pass: find $type discriminator
        string? discriminator = null;
        var tempReader = new DagCborReader(data);
        var count = tempReader.ReadStartMap();
        var remaining = count ?? int.MaxValue;

        while (remaining > 0 && tempReader.PeekState() != CborReaderState.EndMap)
        {
            var key = tempReader.ReadTextString();
            if (key == "$type")
            {
                discriminator = tempReader.ReadTextString();
                break;
            }
            tempReader.SkipValue();
            remaining--;
        }

        // Restore reader position
        reader = new DagCborReader(data);

        if (discriminator != null && DerivedTypes.TryGetValue(discriminator, out var typeInfo))
        {
            return (TInterface?)typeInfo.ReadObject(ref reader);
        }

        // Unknown type - capture the raw data
        if (discriminator != null)
        {
            var startPosition = reader.BytesRead;
            reader.SkipValue(); // Skip the entire map
            var bytesConsumed = reader.BytesRead - startPosition;

            // Reset and capture the raw bytes
            reader = new DagCborReader(data);
            var rawData = data.Slice(0, bytesConsumed).ToArray();
            reader.SkipValue(); // Advance past the map again

            return CreateUnknownInstance(discriminator, rawData);
        }

        throw new InvalidOperationException("Union value missing required $type discriminator");
    }
}
