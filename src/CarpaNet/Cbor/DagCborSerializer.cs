using System;

namespace CarpaNet.Cbor;

/// <summary>
/// AOT-compatible DAG-CBOR serializer and deserializer.
/// Uses CborSerializerContext for type resolution without reflection.
/// </summary>
public sealed class DagCborSerializer
{
    private readonly CborSerializerContext _context;

    /// <summary>
    /// Creates a new DagCborSerializer with the default context.
    /// </summary>
    public DagCborSerializer()
        : this(CborSerializerContext.Default)
    {
    }

    /// <summary>
    /// Creates a new DagCborSerializer with a custom context.
    /// </summary>
    /// <param name="context">The serializer context providing type info.</param>
    public DagCborSerializer(CborSerializerContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Deserializes CBOR data to the specified type.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="data">The CBOR-encoded data.</param>
    /// <returns>The deserialized object.</returns>
    public T? Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        return _context.Deserialize<T>(data);
    }

    /// <summary>
    /// Deserializes CBOR data to the specified type using a reader.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="reader">The CBOR reader.</param>
    /// <returns>The deserialized object.</returns>
    public T? Deserialize<T>(ref DagCborReader reader)
    {
        return _context.DeserializeValue<T>(ref reader);
    }

    /// <summary>
    /// Serializes an object to CBOR data.
    /// </summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The CBOR-encoded data.</returns>
    public byte[] Serialize<T>(T? value)
    {
        return _context.Serialize(value);
    }

    /// <summary>
    /// Serializes an object to a CBOR writer.
    /// </summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="writer">The CBOR writer.</param>
    /// <param name="value">The value to serialize.</param>
    public void Serialize<T>(ref DagCborWriter writer, T? value)
    {
        _context.SerializeValue(ref writer, value);
    }
}
