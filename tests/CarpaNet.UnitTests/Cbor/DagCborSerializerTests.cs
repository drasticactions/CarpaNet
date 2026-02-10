using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using CarpaNet;
using CarpaNet.Cbor;
using CarpaNet.Cbor.Converters;
using Xunit;

namespace CarpaNet.UnitTests.Cbor;

/// <summary>
/// Tests for the AOT-compatible DagCborSerializer and CborSerializerContext.
/// </summary>
public class DagCborSerializerTests
{
    #region Test Types and Type Infos

    public class SimpleObject
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public bool Active { get; set; }
    }

    /// <summary>
    /// Example AOT-compatible type info for SimpleObject.
    /// In a real scenario, this would be source-generated.
    /// </summary>
    public sealed class SimpleObjectTypeInfo : CborObjectTypeInfo<SimpleObject>
    {
        private static readonly CborPropertyInfo<SimpleObject>[] _properties = new CborPropertyInfo<SimpleObject>[]
        {
            new CborPropertyInfo<SimpleObject, string>(
                "name",
                obj => obj.Name,
                (obj, val) => obj.Name = val ?? string.Empty,
                new StringCborConverter(),
                val => val != null),
            new CborPropertyInfo<SimpleObject, int>(
                "count",
                obj => obj.Count,
                (obj, val) => obj.Count = val,
                new Int32CborConverter(),
                _ => true),
            new CborPropertyInfo<SimpleObject, bool>(
                "active",
                obj => obj.Active,
                (obj, val) => obj.Active = val,
                new BooleanCborConverter(),
                _ => true),
        };

        protected override IReadOnlyList<CborPropertyInfo<SimpleObject>> Properties => _properties;

        public override SimpleObject CreateInstance() => new SimpleObject();
    }

    public class ObjectWithAtTypes
    {
        public ATDid Did { get; set; }
        public ATHandle Handle { get; set; }
        public ATUri Uri { get; set; }
    }

    public sealed class ObjectWithAtTypesTypeInfo : CborObjectTypeInfo<ObjectWithAtTypes>
    {
        private static readonly CborPropertyInfo<ObjectWithAtTypes>[] _properties = new CborPropertyInfo<ObjectWithAtTypes>[]
        {
            new CborPropertyInfo<ObjectWithAtTypes, ATDid>(
                "did",
                obj => obj.Did,
                (obj, val) => obj.Did = val,
                new ATDidCborConverter(),
                val => val.Value != null),
            new CborPropertyInfo<ObjectWithAtTypes, ATHandle>(
                "handle",
                obj => obj.Handle,
                (obj, val) => obj.Handle = val,
                new ATHandleCborConverter(),
                val => val.Value != null),
            new CborPropertyInfo<ObjectWithAtTypes, ATUri>(
                "uri",
                obj => obj.Uri,
                (obj, val) => obj.Uri = val,
                new ATUriCborConverter(),
                val => val.Value != null),
        };

        protected override IReadOnlyList<CborPropertyInfo<ObjectWithAtTypes>> Properties => _properties;

        public override ObjectWithAtTypes CreateInstance() => new ObjectWithAtTypes();
    }

    /// <summary>
    /// Test serializer context with registered types.
    /// </summary>
    private sealed class TestSerializerContext : CborSerializerContext
    {
        public TestSerializerContext()
        {
            RegisterTypeInfo(new SimpleObjectTypeInfo());
            RegisterTypeInfo(new ObjectWithAtTypesTypeInfo());
        }
    }

    #endregion

    private readonly DagCborSerializer _serializer;
    private readonly TestSerializerContext _context;

    public DagCborSerializerTests()
    {
        _context = new TestSerializerContext();
        _serializer = new DagCborSerializer(_context);
    }

    [Fact]
    public void Serialize_SimpleObject()
    {
        // Arrange
        var obj = new SimpleObject
        {
            Name = "Test",
            Count = 42,
            Active = true
        };

        // Act
        var data = _serializer.Serialize(obj);

        // Assert
        Assert.NotNull(data);
        Assert.True(data.Length > 0);

        // Verify by deserializing
        var result = _serializer.Deserialize<SimpleObject>(data);
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Count);
        Assert.True(result.Active);
    }

    [Fact]
    public void Deserialize_SimpleObject()
    {
        // Arrange - manually create CBOR data
        var writer = new CborWriter();
        writer.WriteStartMap(3);
        writer.WriteTextString("name");
        writer.WriteTextString("Test");
        writer.WriteTextString("count");
        writer.WriteInt32(42);
        writer.WriteTextString("active");
        writer.WriteBoolean(true);
        writer.WriteEndMap();
        var data = writer.Encode();

        // Act
        var result = _serializer.Deserialize<SimpleObject>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Count);
        Assert.True(result.Active);
    }

    [Fact]
    public void Serialize_ObjectWithAtTypes()
    {
        // Arrange
        var obj = new ObjectWithAtTypes
        {
            Did = new ATDid("did:plc:z72i7hdynmk6r22z27h6tvur"),
            Handle = new ATHandle("alice.bsky.social"),
            Uri = new ATUri("at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/abc")
        };

        // Act
        var data = _serializer.Serialize(obj);

        // Assert
        Assert.NotNull(data);
        Assert.True(data.Length > 0);

        // Verify by deserializing
        var result = _serializer.Deserialize<ObjectWithAtTypes>(data);
        Assert.NotNull(result);
        Assert.Equal("did:plc:z72i7hdynmk6r22z27h6tvur", result.Did.Value);
        Assert.Equal("alice.bsky.social", result.Handle.Value);
        Assert.Equal("at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/abc", result.Uri.Value);
    }

    [Fact]
    public void Deserialize_ObjectWithAtTypes()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteStartMap(3);

        writer.WriteTextString("did");
        writer.WriteTextString("did:plc:z72i7hdynmk6r22z27h6tvur");

        writer.WriteTextString("handle");
        writer.WriteTextString("alice.bsky.social");

        writer.WriteTextString("uri");
        writer.WriteTextString("at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/abc");

        writer.WriteEndMap();
        var data = writer.Encode();

        // Act
        var result = _serializer.Deserialize<ObjectWithAtTypes>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("did:plc:z72i7hdynmk6r22z27h6tvur", result.Did.Value);
        Assert.Equal("alice.bsky.social", result.Handle.Value);
        Assert.Equal("at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.post/abc", result.Uri.Value);
    }

    [Fact]
    public void Serialize_Null()
    {
        // Act
        var data = _serializer.Serialize<SimpleObject>(null);

        // Assert
        Assert.NotNull(data);

        // Verify it's a CBOR null
        var reader = new CborReader(data);
        Assert.Equal(CborReaderState.Null, reader.PeekState());
    }

    [Fact]
    public void Deserialize_Null()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteNull();
        var data = writer.Encode();

        // Act
        var result = _serializer.Deserialize<SimpleObject>(data);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Serialize_PrimitiveTypes()
    {
        // String
        var stringData = _context.Serialize("hello");
        Assert.Equal("hello", _context.Deserialize<string>(stringData));

        // Int
        var intData = _context.Serialize(42);
        Assert.Equal(42, _context.Deserialize<int>(intData));

        // Long
        var longData = _context.Serialize(9999999999L);
        Assert.Equal(9999999999L, _context.Deserialize<long>(longData));

        // Bool
        var boolData = _context.Serialize(true);
        Assert.True(_context.Deserialize<bool>(boolData));

        // Double
        var doubleData = _context.Serialize(3.14);
        Assert.Equal(3.14, _context.Deserialize<double>(doubleData));

        // Bytes
        var byteData = _context.Serialize(new byte[] { 1, 2, 3 });
        Assert.Equal(new byte[] { 1, 2, 3 }, _context.Deserialize<byte[]>(byteData));
    }

    [Fact]
    public void Deserialize_SkipsUnknownProperties()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteStartMap(5);
        writer.WriteTextString("name");
        writer.WriteTextString("Test");
        writer.WriteTextString("unknown1");
        writer.WriteInt32(123);
        writer.WriteTextString("count");
        writer.WriteInt32(5);
        writer.WriteTextString("unknown2");
        writer.WriteStartArray(2);
        writer.WriteTextString("a");
        writer.WriteTextString("b");
        writer.WriteEndArray();
        writer.WriteTextString("active");
        writer.WriteBoolean(true);
        writer.WriteEndMap();
        var data = writer.Encode();

        // Act
        var result = _serializer.Deserialize<SimpleObject>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(5, result.Count);
        Assert.True(result.Active);
    }

    [Fact]
    public void Deserialize_EmptyMap()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteStartMap(0);
        writer.WriteEndMap();
        var data = writer.Encode();

        // Act
        var result = _serializer.Deserialize<SimpleObject>(data);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Name);
        Assert.Equal(0, result.Count);
        Assert.False(result.Active);
    }

    [Fact]
    public void RoundTrip_PreservesData()
    {
        // Arrange
        var original = new SimpleObject
        {
            Name = "Round Trip Test",
            Count = 12345,
            Active = true
        };

        // Act
        var serialized = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<SimpleObject>(serialized);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Count, deserialized.Count);
        Assert.Equal(original.Active, deserialized.Active);
    }

    [Fact]
    public void TypeInfo_ReadObject_ReturnsCorrectType()
    {
        // Arrange
        var writer = new CborWriter();
        writer.WriteStartMap(3);
        writer.WriteTextString("name");
        writer.WriteTextString("TypeInfo Test");
        writer.WriteTextString("count");
        writer.WriteInt32(100);
        writer.WriteTextString("active");
        writer.WriteBoolean(false);
        writer.WriteEndMap();
        var data = writer.Encode();

        var typeInfo = new SimpleObjectTypeInfo();

        // Act
        var result = ((ICborTypeInfo)typeInfo).ReadObject(data);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<SimpleObject>(result);
        var typed = (SimpleObject)result;
        Assert.Equal("TypeInfo Test", typed.Name);
        Assert.Equal(100, typed.Count);
        Assert.False(typed.Active);
    }

    [Fact]
    public void TypeInfo_WriteObject_ProducesValidCbor()
    {
        // Arrange
        var obj = new SimpleObject
        {
            Name = "Write Test",
            Count = 999,
            Active = true
        };
        var typeInfo = new SimpleObjectTypeInfo();

        // Act
        var data = ((ICborTypeInfo)typeInfo).WriteObject(obj);

        // Assert
        Assert.NotNull(data);

        // Verify by reading back
        var reader = new CborReader(data);
        Assert.Equal(CborReaderState.StartMap, reader.PeekState());
    }
}
