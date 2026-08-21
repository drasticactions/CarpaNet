using System;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text.Json;
using CarpaNet;
using CarpaNet.Jetstream;
using Xunit;

namespace CarpaNet.UnitTests.Jetstream;

public class JetstreamV2RecordDecoderTests
{
    private const string SampleCid = "bafyreicqu7jhkc6ec3oq4fexqxlhkr27mjqcbaxkqz6aorpvvxwfkmmf3u";

    private static byte[] SampleRecordCbor()
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(5);
        writer.WriteTextString("n");
        writer.WriteInt64(42);
        writer.WriteTextString("arr");
        writer.WriteStartArray(3);
        writer.WriteInt64(0);
        writer.WriteBoolean(true);
        writer.WriteNull();
        writer.WriteEndArray();
        writer.WriteTextString("bytes");
        writer.WriteByteString(new byte[] { 1, 2 });
        writer.WriteTextString("link");
        writer.WriteTag((CborTag)42);
        var cidBytes = new ATCid(SampleCid).ToBytes();
        var tagged = new byte[cidBytes.Length + 1];
        cidBytes.CopyTo(tagged, 1); // DAG-CBOR CID byte strings carry a 0x00 multibase prefix
        writer.WriteByteString(tagged);
        writer.WriteTextString("text");
        writer.WriteTextString("hi");
        writer.WriteEndMap();
        return writer.Encode();
    }

    [Fact]
    public void CborToJsonElement_ProducesAtprotoJsonShape()
    {
        var element = JetstreamV2RecordDecoder.CborToJsonElement(SampleRecordCbor());

        Assert.Equal(42, element.GetProperty("n").GetInt64());
        Assert.Equal("hi", element.GetProperty("text").GetString());

        var arr = element.GetProperty("arr");
        Assert.Equal(3, arr.GetArrayLength());
        Assert.Equal(0, arr[0].GetInt64());
        Assert.True(arr[1].GetBoolean());
        Assert.Equal(JsonValueKind.Null, arr[2].ValueKind);

        // Byte strings surface as {"$bytes": base64-without-padding}.
        Assert.Equal("AQI", element.GetProperty("bytes").GetProperty("$bytes").GetString());

        // CID links surface as {"$link": cid}.
        Assert.Equal(SampleCid, element.GetProperty("link").GetProperty("$link").GetString());
    }

    [Fact]
    public void CborToJsonElement_RejectsNonMapTopLevel()
    {
        var writer = new CborWriter();
        writer.WriteInt64(5);

        Assert.Throws<JetstreamV2Exception>(() => JetstreamV2RecordDecoder.CborToJsonElement(writer.Encode()));
    }

    [Fact]
    public void CborToJsonElement_RejectsFloats()
    {
        // The atproto data model has integers, not floats.
        var writer = new CborWriter();
        writer.WriteStartMap(1);
        writer.WriteTextString("f");
        writer.WriteDouble(1.5);
        writer.WriteEndMap();

        Assert.Throws<JetstreamV2Exception>(() => JetstreamV2RecordDecoder.CborToJsonElement(writer.Encode()));
    }

    [Fact]
    public void CborToJsonElement_RejectsTrailingBytes()
    {
        var payload = SampleRecordCbor();
        var padded = new byte[payload.Length + 1];
        payload.CopyTo(padded, 0);
        padded[payload.Length] = 0xA0;

        Assert.Throws<JetstreamV2Exception>(() => JetstreamV2RecordDecoder.CborToJsonElement(padded));
    }

    [Fact]
    public void ConvertRow_CreateCommit_DecodesRecordAndComputesCid()
    {
        var payload = SampleRecordCbor();
        var row = new JetstreamSegmentRow
        {
            Seq = 9,
            WitnessedAt = 123,
            Kind = JetstreamSegmentRowKind.Create,
            Did = "did:plc:a",
            Collection = "app.bsky.feed.post",
            Rkey = "rk",
            Rev = "rv",
            Payload = payload,
        };

        var evt = JetstreamV2RecordDecoder.ConvertRow(row);

        Assert.Equal(JetstreamV2EventKind.Commit, evt.Kind);
        Assert.Equal(9, evt.Seq);
        Assert.Equal(123, evt.TimeUs);
        var commit = evt.Commit!;
        Assert.Equal(JetstreamV2CommitOperation.Create, commit.Operation);
        Assert.Equal("hi", commit.Record!.Value.GetProperty("text").GetString());

        using var sha = SHA256.Create();
        var expectedCid = ATCid.FromSha256Hash(sha.ComputeHash(payload)).Value;
        Assert.Equal(expectedCid, commit.Cid);
    }

    [Fact]
    public void ConvertRow_CreateResync_RendersAsCreate()
    {
        var row = new JetstreamSegmentRow
        {
            Seq = 1,
            Kind = JetstreamSegmentRowKind.CreateResync,
            Did = "did:plc:a",
            Collection = "c.o.l",
            Rkey = "rk",
            Rev = "rv",
            Payload = SampleRecordCbor(),
        };

        Assert.Equal(JetstreamV2CommitOperation.Create, JetstreamV2RecordDecoder.ConvertRow(row).Commit!.Operation);
    }

    [Fact]
    public void ConvertRow_DeleteCommit_HasNoRecord()
    {
        var row = new JetstreamSegmentRow
        {
            Seq = 2,
            Kind = JetstreamSegmentRowKind.Delete,
            Did = "did:plc:a",
            Collection = "c.o.l",
            Rkey = "rk",
            Rev = "rv",
        };

        var commit = JetstreamV2RecordDecoder.ConvertRow(row).Commit!;
        Assert.Equal(JetstreamV2CommitOperation.Delete, commit.Operation);
        Assert.Null(commit.Record);
        Assert.Null(commit.Cid);
    }

    [Fact]
    public void ConvertRow_CreateWithoutPayload_Throws()
    {
        var row = new JetstreamSegmentRow
        {
            Seq = 3,
            Kind = JetstreamSegmentRowKind.Create,
            Did = "did:plc:a",
            Collection = "c.o.l",
            Rkey = "rk",
            Rev = "rv",
        };

        Assert.Throws<JetstreamV2Exception>(() => JetstreamV2RecordDecoder.ConvertRow(row));
    }

    [Fact]
    public void ConvertRow_Identity_DecodesUpstreamPayload()
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(4);
        writer.WriteTextString("did");
        writer.WriteTextString("did:plc:a");
        writer.WriteTextString("seq");
        writer.WriteInt64(700);
        writer.WriteTextString("time");
        writer.WriteTextString("2026-05-25T00:00:00Z");
        writer.WriteTextString("handle");
        writer.WriteTextString("alice.test");
        writer.WriteEndMap();

        var row = new JetstreamSegmentRow
        {
            Seq = 4,
            WitnessedAt = 55,
            Kind = JetstreamSegmentRowKind.Identity,
            Did = "did:plc:a",
            Payload = writer.Encode(),
        };

        var evt = JetstreamV2RecordDecoder.ConvertRow(row);
        Assert.Equal(JetstreamV2EventKind.Identity, evt.Kind);
        Assert.Equal("alice.test", evt.Identity!.Handle);
        Assert.Equal(700, evt.Identity.Seq);
        Assert.Equal("2026-05-25T00:00:00Z", evt.Identity.Time);
    }

    [Fact]
    public void ConvertRow_Account_DecodesUpstreamPayloadAndSkipsUnknownKeys()
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(5);
        writer.WriteTextString("did");
        writer.WriteTextString("did:plc:a");
        writer.WriteTextString("seq");
        writer.WriteInt64(701);
        writer.WriteTextString("time");
        writer.WriteTextString("2026-05-25T00:00:00Z");
        writer.WriteTextString("active");
        writer.WriteBoolean(false);
        writer.WriteTextString("status");
        writer.WriteTextString("deleted");
        writer.WriteEndMap();

        var row = new JetstreamSegmentRow
        {
            Seq = 5,
            Kind = JetstreamSegmentRowKind.Account,
            Did = "did:plc:a",
            Payload = writer.Encode(),
        };

        var evt = JetstreamV2RecordDecoder.ConvertRow(row);
        Assert.False(evt.Account!.Active);
        Assert.Equal("deleted", evt.Account.Status);
        Assert.Equal(701, evt.Account.Seq);
    }

    [Fact]
    public void ConvertRow_Sync_IgnoresBlocksBytes()
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(5);
        writer.WriteTextString("did");
        writer.WriteTextString("did:plc:a");
        writer.WriteTextString("rev");
        writer.WriteTextString("rev1");
        writer.WriteTextString("seq");
        writer.WriteInt64(800);
        writer.WriteTextString("time");
        writer.WriteTextString("2026-05-25T00:00:00Z");
        writer.WriteTextString("blocks");
        writer.WriteByteString(new byte[] { 1, 2, 3 });
        writer.WriteEndMap();

        var row = new JetstreamSegmentRow
        {
            Seq = 6,
            Kind = JetstreamSegmentRowKind.Sync,
            Did = "did:plc:a",
            Payload = writer.Encode(),
        };

        var evt = JetstreamV2RecordDecoder.ConvertRow(row);
        Assert.Equal("rev1", evt.Sync!.Rev);
        Assert.Equal(800, evt.Sync.Seq);
    }
}
