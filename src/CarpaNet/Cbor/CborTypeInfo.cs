using System;
using System.Collections.Generic;

namespace CarpaNet.Cbor;

/// <summary>
/// Non-generic interface for type-erased operations.
/// </summary>
public interface ICborTypeInfo
{
    /// <summary>
    /// Gets the type this info describes.
    /// </summary>
    Type TargetType { get; }

    /// <summary>
    /// Gets the type discriminator value for polymorphic serialization.
    /// </summary>
    string? TypeDiscriminator { get; }

    /// <summary>
    /// Reads an instance from the CBOR reader (type-erased).
    /// </summary>
    object? ReadObject(ref DagCborReader reader);

    /// <summary>
    /// Reads an instance from CBOR data (type-erased, for use in async contexts).
    /// </summary>
    object? ReadObject(ReadOnlyMemory<byte> data);

    /// <summary>
    /// Writes an instance to the CBOR writer (type-erased).
    /// </summary>
    void WriteObject(ref DagCborWriter writer, object? value);

    /// <summary>
    /// Serializes an instance to CBOR data (type-erased, for use in async contexts).
    /// </summary>
    byte[] WriteObject(object? value);
}

/// <summary>
/// Provides metadata for serializing and deserializing a type to/from DAG-CBOR.
/// </summary>
/// <typeparam name="T">The type this info describes.</typeparam>
public abstract class CborTypeInfo<T> : ICborTypeInfo
{
    /// <inheritdoc/>
    public Type TargetType => typeof(T);

    /// <summary>
    /// Gets the type discriminator value for polymorphic serialization (e.g., "com.atproto.repo.strongRef").
    /// </summary>
    public virtual string? TypeDiscriminator => null;

    /// <inheritdoc/>
    string? ICborTypeInfo.TypeDiscriminator => TypeDiscriminator;

    /// <summary>
    /// Creates a new instance of the type.
    /// </summary>
    public abstract T CreateInstance();

    /// <summary>
    /// Reads an instance from the CBOR reader.
    /// </summary>
    /// <param name="reader">The CBOR reader.</param>
    /// <returns>The deserialized instance.</returns>
    public abstract T? Read(ref DagCborReader reader);

    /// <summary>
    /// Writes an instance to the CBOR writer.
    /// </summary>
    /// <param name="writer">The CBOR writer.</param>
    /// <param name="value">The value to serialize.</param>
    public abstract void Write(ref DagCborWriter writer, T? value);

    /// <inheritdoc/>
    object? ICborTypeInfo.ReadObject(ref DagCborReader reader) => Read(ref reader);

    /// <inheritdoc/>
    object? ICborTypeInfo.ReadObject(ReadOnlyMemory<byte> data)
    {
        var reader = new DagCborReader(data);
        return Read(ref reader);
    }

    /// <inheritdoc/>
    void ICborTypeInfo.WriteObject(ref DagCborWriter writer, object? value) => Write(ref writer, (T?)value);

    /// <inheritdoc/>
    byte[] ICborTypeInfo.WriteObject(object? value)
    {
        var writer = new DagCborWriter();
        Write(ref writer, (T?)value);
        return writer.Encode();
    }
}

/// <summary>
/// Base class for object type info that handles map-based CBOR structures.
/// </summary>
/// <typeparam name="T">The type this info describes.</typeparam>
public abstract class CborObjectTypeInfo<T> : CborTypeInfo<T> where T : class
{
    /// <summary>
    /// Gets the property infos for this type.
    /// </summary>
    protected abstract IReadOnlyList<CborPropertyInfo<T>> Properties { get; }

    /// <inheritdoc/>
    public override T? Read(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == System.Formats.Cbor.CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        if (state != System.Formats.Cbor.CborReaderState.StartMap)
        {
            throw new InvalidOperationException($"Expected map for {typeof(T).Name}, got {state}");
        }

        var instance = CreateInstance();
        var count = reader.ReadStartMap();
        var remaining = count ?? int.MaxValue;

        while (remaining > 0 && reader.PeekState() != System.Formats.Cbor.CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();

            if (key == "$type")
            {
                // Skip the type discriminator
                reader.SkipValue();
            }
            else
            {
                var found = false;
                foreach (var prop in Properties)
                {
                    if (prop.Name == key)
                    {
                        prop.ReadAndSet(ref reader, instance);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // Skip unknown properties
                    reader.SkipValue();
                }
            }

            remaining--;
        }

        reader.ReadEndMap();
        return instance;
    }

    /// <inheritdoc/>
    public override void Write(ref DagCborWriter writer, T? value)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        // Count non-null properties
        var propertyCount = TypeDiscriminator != null ? 1 : 0;
        foreach (var prop in Properties)
        {
            if (prop.ShouldSerialize(value))
            {
                propertyCount++;
            }
        }

        writer.WriteStartMap(propertyCount);

        // Write $type if present
        if (TypeDiscriminator != null)
        {
            writer.WriteTextString("$type");
            writer.WriteTextString(TypeDiscriminator);
        }

        // Write properties
        foreach (var prop in Properties)
        {
            if (prop.ShouldSerialize(value))
            {
                writer.WriteTextString(prop.Name);
                prop.GetAndWrite(ref writer, value);
            }
        }

        writer.WriteEndMap();
    }
}

/// <summary>
/// Provides metadata for a single property of a type.
/// </summary>
/// <typeparam name="TObject">The type that contains this property.</typeparam>
public abstract class CborPropertyInfo<TObject>
{
    /// <summary>
    /// Gets the CBOR property name (from JsonPropertyName or property name).
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Reads the property value from the reader and sets it on the object.
    /// </summary>
    public abstract void ReadAndSet(ref DagCborReader reader, TObject obj);

    /// <summary>
    /// Gets the property value from the object and writes it to the writer.
    /// </summary>
    public abstract void GetAndWrite(ref DagCborWriter writer, TObject obj);

    /// <summary>
    /// Determines if the property should be serialized (non-null for reference types).
    /// </summary>
    public abstract bool ShouldSerialize(TObject obj);
}

/// <summary>
/// Strongly-typed property info for AOT serialization.
/// </summary>
/// <typeparam name="TObject">The type that contains this property.</typeparam>
/// <typeparam name="TProperty">The property type.</typeparam>
public sealed class CborPropertyInfo<TObject, TProperty> : CborPropertyInfo<TObject>
{
    private readonly string _name;
    private readonly Func<TObject, TProperty> _getter;
    private readonly Action<TObject, TProperty> _setter;
    private readonly Func<TProperty, bool> _shouldSerialize;
    private readonly CborTypeInfo<TProperty>? _typeInfo;
    private readonly ICborTypeConverter<TProperty>? _converter;

    /// <summary>
    /// Creates a new property info with a type info.
    /// </summary>
    public CborPropertyInfo(
        string name,
        Func<TObject, TProperty> getter,
        Action<TObject, TProperty> setter,
        CborTypeInfo<TProperty> typeInfo,
        Func<TProperty, bool>? shouldSerialize = null)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _getter = getter ?? throw new ArgumentNullException(nameof(getter));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _typeInfo = typeInfo ?? throw new ArgumentNullException(nameof(typeInfo));
        _shouldSerialize = shouldSerialize ?? DefaultShouldSerialize;
    }

    /// <summary>
    /// Creates a new property info with a converter.
    /// </summary>
    public CborPropertyInfo(
        string name,
        Func<TObject, TProperty> getter,
        Action<TObject, TProperty> setter,
        ICborTypeConverter<TProperty> converter,
        Func<TProperty, bool>? shouldSerialize = null)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _getter = getter ?? throw new ArgumentNullException(nameof(getter));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _shouldSerialize = shouldSerialize ?? DefaultShouldSerialize;
    }

    /// <inheritdoc/>
    public override string Name => _name;

    /// <inheritdoc/>
    public override void ReadAndSet(ref DagCborReader reader, TObject obj)
    {
        TProperty? value;
        if (_typeInfo != null)
        {
            value = _typeInfo.Read(ref reader);
        }
        else if (_converter != null)
        {
            value = _converter.ReadTyped(ref reader);
        }
        else
        {
            throw new InvalidOperationException($"No type info or converter for property {_name}");
        }

        _setter(obj, value!);
    }

    /// <inheritdoc/>
    public override void GetAndWrite(ref DagCborWriter writer, TObject obj)
    {
        var value = _getter(obj);
        if (_typeInfo != null)
        {
            _typeInfo.Write(ref writer, value);
        }
        else if (_converter != null)
        {
            _converter.WriteTyped(ref writer, value);
        }
        else
        {
            throw new InvalidOperationException($"No type info or converter for property {_name}");
        }
    }

    /// <inheritdoc/>
    public override bool ShouldSerialize(TObject obj)
    {
        var value = _getter(obj);
        return _shouldSerialize(value);
    }

    private static bool DefaultShouldSerialize(TProperty value)
    {
        if (value == null)
        {
            return false;
        }

        // For nullable value types, check if they have a value
        if (typeof(TProperty).IsGenericType &&
            typeof(TProperty).GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return true; // Already checked for null above
        }

        return true;
    }
}

/// <summary>
/// Type info for array types.
/// </summary>
/// <typeparam name="TElement">The element type.</typeparam>
public sealed class CborArrayTypeInfo<TElement> : CborTypeInfo<TElement[]>
{
    private readonly CborTypeInfo<TElement>? _elementTypeInfo;
    private readonly ICborTypeConverter<TElement>? _elementConverter;

    /// <summary>
    /// Creates a new array type info with element type info.
    /// </summary>
    public CborArrayTypeInfo(CborTypeInfo<TElement> elementTypeInfo)
    {
        _elementTypeInfo = elementTypeInfo ?? throw new ArgumentNullException(nameof(elementTypeInfo));
    }

    /// <summary>
    /// Creates a new array type info with element converter.
    /// </summary>
    public CborArrayTypeInfo(ICborTypeConverter<TElement> elementConverter)
    {
        _elementConverter = elementConverter ?? throw new ArgumentNullException(nameof(elementConverter));
    }

    /// <inheritdoc/>
    public override TElement[] CreateInstance() => Array.Empty<TElement>();

    /// <inheritdoc/>
    public override TElement[]? Read(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == System.Formats.Cbor.CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        var count = reader.ReadStartArray();
        var list = new List<TElement>();
        var remaining = count ?? int.MaxValue;

        while (remaining > 0 && reader.PeekState() != System.Formats.Cbor.CborReaderState.EndArray)
        {
            TElement? element;
            if (_elementTypeInfo != null)
            {
                element = _elementTypeInfo.Read(ref reader);
            }
            else if (_elementConverter != null)
            {
                element = _elementConverter.ReadTyped(ref reader);
            }
            else
            {
                throw new InvalidOperationException("No element type info or converter");
            }

            list.Add(element!);
            remaining--;
        }

        reader.ReadEndArray();
        return list.ToArray();
    }

    /// <inheritdoc/>
    public override void Write(ref DagCborWriter writer, TElement[]? value)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartArray(value.Length);

        foreach (var element in value)
        {
            if (_elementTypeInfo != null)
            {
                _elementTypeInfo.Write(ref writer, element);
            }
            else if (_elementConverter != null)
            {
                _elementConverter.WriteTyped(ref writer, element);
            }
        }

        writer.WriteEndArray();
    }
}

/// <summary>
/// Type info for List types.
/// </summary>
/// <typeparam name="TElement">The element type.</typeparam>
public sealed class CborListTypeInfo<TElement> : CborTypeInfo<List<TElement>>
{
    private readonly CborTypeInfo<TElement>? _elementTypeInfo;
    private readonly ICborTypeConverter<TElement>? _elementConverter;

    /// <summary>
    /// Creates a new list type info with element type info.
    /// </summary>
    public CborListTypeInfo(CborTypeInfo<TElement> elementTypeInfo)
    {
        _elementTypeInfo = elementTypeInfo ?? throw new ArgumentNullException(nameof(elementTypeInfo));
    }

    /// <summary>
    /// Creates a new list type info with element converter.
    /// </summary>
    public CborListTypeInfo(ICborTypeConverter<TElement> elementConverter)
    {
        _elementConverter = elementConverter ?? throw new ArgumentNullException(nameof(elementConverter));
    }

    /// <inheritdoc/>
    public override List<TElement> CreateInstance() => new List<TElement>();

    /// <inheritdoc/>
    public override List<TElement>? Read(ref DagCborReader reader)
    {
        var state = reader.PeekState();
        if (state == System.Formats.Cbor.CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        var count = reader.ReadStartArray();
        var list = new List<TElement>();
        var remaining = count ?? int.MaxValue;

        while (remaining > 0 && reader.PeekState() != System.Formats.Cbor.CborReaderState.EndArray)
        {
            TElement? element;
            if (_elementTypeInfo != null)
            {
                element = _elementTypeInfo.Read(ref reader);
            }
            else if (_elementConverter != null)
            {
                element = _elementConverter.ReadTyped(ref reader);
            }
            else
            {
                throw new InvalidOperationException("No element type info or converter");
            }

            list.Add(element!);
            remaining--;
        }

        reader.ReadEndArray();
        return list;
    }

    /// <inheritdoc/>
    public override void Write(ref DagCborWriter writer, List<TElement>? value)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartArray(value.Count);

        foreach (var element in value)
        {
            if (_elementTypeInfo != null)
            {
                _elementTypeInfo.Write(ref writer, element);
            }
            else if (_elementConverter != null)
            {
                _elementConverter.WriteTyped(ref writer, element);
            }
        }

        writer.WriteEndArray();
    }
}
