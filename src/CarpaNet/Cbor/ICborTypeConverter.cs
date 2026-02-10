using System;

namespace CarpaNet.Cbor;

/// <summary>
/// Interface for type-specific CBOR converters (AOT-compatible).
/// </summary>
public interface ICborTypeConverter
{
    /// <summary>
    /// Gets the type this converter handles.
    /// </summary>
    Type TargetType { get; }

    /// <summary>
    /// Determines if this converter can handle the specified type.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if this converter can handle the type.</returns>
    bool CanConvert(Type type);

    /// <summary>
    /// Reads a value from the CBOR reader.
    /// </summary>
    /// <param name="reader">The CBOR reader.</param>
    /// <param name="targetType">The target type to deserialize to.</param>
    /// <returns>The deserialized value.</returns>
    object? Read(ref DagCborReader reader, Type targetType);

    /// <summary>
    /// Writes a value to the CBOR writer.
    /// </summary>
    /// <param name="writer">The CBOR writer.</param>
    /// <param name="value">The value to serialize.</param>
    void Write(ref DagCborWriter writer, object? value);
}

/// <summary>
/// Generic interface for type-specific CBOR converters (AOT-compatible).
/// </summary>
/// <typeparam name="T">The type this converter handles.</typeparam>
public interface ICborTypeConverter<T> : ICborTypeConverter
{
    /// <summary>
    /// Reads a value of type T from the CBOR reader.
    /// </summary>
    /// <param name="reader">The CBOR reader.</param>
    /// <returns>The deserialized value.</returns>
    T? ReadTyped(ref DagCborReader reader);

    /// <summary>
    /// Writes a value of type T to the CBOR writer.
    /// </summary>
    /// <param name="writer">The CBOR writer.</param>
    /// <param name="value">The value to serialize.</param>
    void WriteTyped(ref DagCborWriter writer, T? value);
}

/// <summary>
/// Base class for type-specific CBOR converters (AOT-compatible).
/// </summary>
/// <typeparam name="T">The type this converter handles.</typeparam>
public abstract class CborTypeConverter<T> : ICborTypeConverter<T>
{
    /// <inheritdoc/>
    public Type TargetType => typeof(T);

    /// <inheritdoc/>
    public virtual bool CanConvert(Type type) => type == typeof(T);

    /// <inheritdoc/>
    public abstract T? ReadTyped(ref DagCborReader reader);

    /// <inheritdoc/>
    public abstract void WriteTyped(ref DagCborWriter writer, T? value);

    /// <inheritdoc/>
    object? ICborTypeConverter.Read(ref DagCborReader reader, Type targetType) => ReadTyped(ref reader);

    /// <inheritdoc/>
    void ICborTypeConverter.Write(ref DagCborWriter writer, object? value) => WriteTyped(ref writer, (T?)value);
}
