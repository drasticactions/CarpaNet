using System.Text.Json;
using CarpaNet.Cbor;
using CarpaNet.Cbor.Converters;
using Xunit;

namespace CarpaNet.UnitTests.Cbor;

/// <summary>
/// DAG-CBOR requires map keys sorted by encoded length first, then bytewise.
/// </summary>
public class DagCborCanonicalOrderTests
{
    [Fact]
    public void KeyComparer_SortsLengthFirstThenBytewise()
    {
        var keys = new List<string> { "createdAt", "$type", "text", "langs", "embed", "a", "bb", "aa", "tags", "reply" };
        keys.Sort(DagCborKeyComparer.Instance);

        Assert.Equal(new[] { "a", "aa", "bb", "tags", "text", "$type", "embed", "langs", "reply", "createdAt" }, keys);
    }

    [Fact]
    public void KeyComparer_ComparesUtf8Bytes()
    {
        // "é" is 2 UTF-8 bytes, so it sorts after any 1-byte key even though it is one UTF-16 char
        Assert.True(DagCborKeyComparer.Instance.Compare("z", "é") < 0);
        Assert.True(DagCborKeyComparer.Instance.Compare("é", "zz") > 0);
        Assert.True(DagCborKeyComparer.Instance.Compare("￿", "\U0001F600") < 0); // 3 bytes vs 4 bytes
        Assert.Equal(0, DagCborKeyComparer.Instance.Compare("same", "same"));
    }

    [Fact]
    public void Blob_IsWrittenInCanonicalKeyOrder()
    {
        var blob = new ATBlob(new ATCid("bafkreieq5jui4j25lacwomsqgjeswwl3y5zcdrresptwgmfylxo2depppq"), "image/jpeg", 12345);
        var writer = new DagCborWriter();
        new ATBlobCborConverter().WriteTyped(ref writer, blob);

        var reader = new DagCborReader(writer.Encode());
        Assert.Equal(4, reader.ReadStartMap());
        Assert.Equal("ref", reader.ReadTextString());
        Assert.Equal(blob.Ref.Value, reader.ReadCidLink().Value);
        Assert.Equal("size", reader.ReadTextString());
        Assert.Equal(12345, reader.ReadInt64());
        Assert.Equal("$type", reader.ReadTextString());
        Assert.Equal("blob", reader.ReadTextString());
        Assert.Equal("mimeType", reader.ReadTextString());
        Assert.Equal("image/jpeg", reader.ReadTextString());
        reader.ReadEndMap();
    }

    [Fact]
    public void JsonElement_ObjectsAreWrittenInCanonicalKeyOrder()
    {
        using var doc = JsonDocument.Parse("""{"bb":1,"a":2,"$type":"x","c":3,"aa":{"zz":1,"y":2}}""");
        var writer = new DagCborWriter();
        new JsonElementCborConverter().WriteTyped(ref writer, doc.RootElement);

        // map(5) "a" 2 "c" 3 "aa" {"y" 2 "zz" 1} "bb" 1 "$type" "x"
        Assert.Equal("A5616102616303626161A2617902627A7A01626262016524747970656178", Convert.ToHexString(writer.Encode()));
    }

    [Fact]
    public void JsonElement_LinkAndBytesObjectsAreWrittenAsCidLinkAndByteString()
    {
        const string cid = "bafkreieq5jui4j25lacwomsqgjeswwl3y5zcdrresptwgmfylxo2depppq";
        using var doc = JsonDocument.Parse("{\"l\":{\"$link\":\"" + cid + "\"},\"b\":{\"$bytes\":\"AQID\"},\"n\":{\"$link\":1}}");
        var writer = new DagCborWriter();
        new JsonElementCborConverter().WriteTyped(ref writer, doc.RootElement);

        var reader = new DagCborReader(writer.Encode());
        Assert.Equal(3, reader.ReadStartMap());
        Assert.Equal("b", reader.ReadTextString());
        Assert.Equal(new byte[] { 1, 2, 3 }, reader.ReadByteString());
        Assert.Equal("l", reader.ReadTextString());
        Assert.Equal(cid, reader.ReadCidLink().Value);
        Assert.Equal("n", reader.ReadTextString());
        Assert.Equal(1, reader.ReadStartMap()); // not a valid link: kept as a map
    }

    [Fact]
    public void DateTimeOffset_IsWrittenAsUtcMilliseconds()
    {
        var writer = new DagCborWriter();
        new DateTimeOffsetCborConverter().WriteTyped(ref writer, new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.FromHours(2)).AddTicks(1234567));

        var reader = new DagCborReader(writer.Encode());
        Assert.Equal("2024-01-01T00:00:00.123Z", reader.ReadTextString());
    }

    [Theory]
    [InlineData("2024-01-01T00:00:00.123456789Z")]
    [InlineData("2024-01-01T00:00:00.123Z")]
    [InlineData("2024-01-01T02:00:00.123+02:00")]
    [InlineData("2024-01-01T00:00:00.123-00:00")]
    public void DateTimeOffset_ReadsAtprotoDatetimes(string value)
    {
        var writer = new DagCborWriter();
        writer.WriteTextString(value);
        var reader = new DagCborReader(writer.Encode());

        var result = new DateTimeOffsetCborConverter().ReadTyped(ref reader);
        Assert.Equal("2024-01-01T00:00:00.123Z", ATDateTimeJsonConverter.Format(result));
    }
}
