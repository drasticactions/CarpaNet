using System;
using System.Collections.Generic;
using CarpaNet.Cbor.Converters;

namespace CarpaNet.Cbor;

/// <summary>
/// Abstract base class for CBOR serialization contexts.
/// Derived classes should be source-generated to provide type info for all serializable types.
/// </summary>
public abstract class CborSerializerContext
{
    private readonly Dictionary<Type, ICborTypeConverter> _converters = new();
    private readonly Dictionary<Type, ICborTypeInfo> _typeInfos = new();
    private readonly Dictionary<string, Type> _discriminatorToType = new();

    /// <summary>
    /// Gets the default context with primitive converters only.
    /// </summary>
    public static CborSerializerContext Default { get; } = new DefaultCborSerializerContext();

    /// <summary>
    /// Initializes a new instance of the CborSerializerContext.
    /// </summary>
    protected CborSerializerContext()
    {
        // Register primitive converters
        RegisterConverter(new StringCborConverter());
        RegisterConverter(new Int32CborConverter());
        RegisterConverter(new Int64CborConverter());
        RegisterConverter(new UInt32CborConverter());
        RegisterConverter(new UInt64CborConverter());
        RegisterConverter(new BooleanCborConverter());
        RegisterConverter(new DoubleCborConverter());
        RegisterConverter(new SingleCborConverter());
        RegisterConverter(new ByteArrayCborConverter());
        RegisterConverter(new DateTimeOffsetCborConverter());
        RegisterConverter(new NullableDateTimeOffsetCborConverter());
        RegisterConverter(new NullableInt32CborConverter());
        RegisterConverter(new NullableInt64CborConverter());
        RegisterConverter(new NullableBooleanCborConverter());
        RegisterConverter(new NullableDoubleCborConverter());

        // Register AT Protocol converters
        RegisterConverter(new ATCidCborConverter());
        RegisterConverter(new NullableATCidCborConverter());
        RegisterConverter(new ATDidCborConverter());
        RegisterConverter(new NullableATDidCborConverter());
        RegisterConverter(new ATHandleCborConverter());
        RegisterConverter(new NullableATHandleCborConverter());
        RegisterConverter(new ATUriCborConverter());
        RegisterConverter(new NullableATUriCborConverter());
        RegisterConverter(new ATIdentifierCborConverter());
        RegisterConverter(new NullableATIdentifierCborConverter());
        RegisterConverter(new ATBlobCborConverter());
    }

    /// <summary>
    /// Registers a type converter.
    /// </summary>
    protected void RegisterConverter(ICborTypeConverter converter)
    {
        _converters[converter.TargetType] = converter;
    }

    /// <summary>
    /// Registers a type converter.
    /// </summary>
    protected void RegisterConverter<T>(ICborTypeConverter<T> converter)
    {
        _converters[typeof(T)] = converter;
    }

    /// <summary>
    /// Registers type info for a type.
    /// </summary>
    protected void RegisterTypeInfo<T>(CborTypeInfo<T> typeInfo)
    {
        _typeInfos[typeof(T)] = typeInfo;
        if (typeInfo.TypeDiscriminator != null)
        {
            _discriminatorToType[typeInfo.TypeDiscriminator] = typeof(T);
        }
    }

    /// <summary>
    /// Registers a type discriminator to type mapping for polymorphic deserialization.
    /// </summary>
    protected void RegisterDerivedType(string discriminator, Type type)
    {
        _discriminatorToType[discriminator] = type;
    }

    /// <summary>
    /// Gets the converter for the specified type, if available.
    /// </summary>
    public bool TryGetConverter(Type type, out ICborTypeConverter? converter)
    {
        return _converters.TryGetValue(type, out converter);
    }

    /// <summary>
    /// Gets the converter for the specified type, if available.
    /// </summary>
    public bool TryGetConverter<T>(out ICborTypeConverter<T>? converter)
    {
        if (_converters.TryGetValue(typeof(T), out var rawConverter) && rawConverter is ICborTypeConverter<T> typed)
        {
            converter = typed;
            return true;
        }

        converter = null;
        return false;
    }

    /// <summary>
    /// Gets the type info for the specified type, if available.
    /// </summary>
    public bool TryGetTypeInfo<T>(out CborTypeInfo<T>? typeInfo)
    {
        if (_typeInfos.TryGetValue(typeof(T), out var raw) && raw is CborTypeInfo<T> typed)
        {
            typeInfo = typed;
            return true;
        }

        typeInfo = null;
        return false;
    }

    /// <summary>
    /// Gets the type info for the specified type, if available.
    /// </summary>
    public bool TryGetTypeInfo(Type type, out ICborTypeInfo? typeInfo)
    {
        return _typeInfos.TryGetValue(type, out typeInfo);
    }

    /// <summary>
    /// Gets the type for a type discriminator, if available.
    /// </summary>
    public bool TryGetTypeFromDiscriminator(string discriminator, out Type? type)
    {
        return _discriminatorToType.TryGetValue(discriminator, out type);
    }

    /// <summary>
    /// Gets the type for a type discriminator, checking for suffix matches.
    /// </summary>
    public bool TryGetTypeFromDiscriminatorWithSuffix(string discriminator, out Type? type)
    {
        if (_discriminatorToType.TryGetValue(discriminator, out type))
        {
            return true;
        }

        // Try suffix match (e.g., "#commit" matches "com.atproto.sync.subscribeRepos#commit")
        foreach (var kvp in _discriminatorToType)
        {
            if (kvp.Key.EndsWith(discriminator, StringComparison.Ordinal))
            {
                type = kvp.Value;
                return true;
            }
        }

        type = null;
        return false;
    }

    /// <summary>
    /// Deserializes a value from CBOR data.
    /// </summary>
    public T? Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        var reader = new DagCborReader(data);
        return DeserializeValue<T>(ref reader);
    }

    /// <summary>
    /// Serializes a value to CBOR data.
    /// </summary>
    public byte[] Serialize<T>(T? value)
    {
        var writer = new DagCborWriter();
        SerializeValue(ref writer, value);
        return writer.Encode();
    }

    /// <summary>
    /// Deserializes a value from the reader.
    /// </summary>
    public T? DeserializeValue<T>(ref DagCborReader reader)
    {
        // Check for null
        if (reader.PeekState() == System.Formats.Cbor.CborReaderState.Null)
        {
            reader.ReadNull();
            return default;
        }

        // Try type info first
        if (TryGetTypeInfo<T>(out var typeInfo))
        {
            return typeInfo!.Read(ref reader);
        }

        // Try converter
        if (TryGetConverter<T>(out var converter))
        {
            return converter!.ReadTyped(ref reader);
        }

        throw new InvalidOperationException($"No type info or converter registered for type {typeof(T).Name}. " +
            "Ensure the type is registered in your CborSerializerContext.");
    }

    /// <summary>
    /// Serializes a value to the writer.
    /// </summary>
    public void SerializeValue<T>(ref DagCborWriter writer, T? value)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        // Try type info first
        if (TryGetTypeInfo<T>(out var typeInfo))
        {
            typeInfo!.Write(ref writer, value);
            return;
        }

        // Try converter
        if (TryGetConverter<T>(out var converter))
        {
            converter!.WriteTyped(ref writer, value);
            return;
        }

        throw new InvalidOperationException($"No type info or converter registered for type {typeof(T).Name}. " +
            "Ensure the type is registered in your CborSerializerContext.");
    }
}

/// <summary>
/// Default context with only primitive and AT Protocol type converters.
/// </summary>
internal sealed class DefaultCborSerializerContext : CborSerializerContext
{
}
