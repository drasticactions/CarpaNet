using System.Text;
using System.Text.Json;
using CarpaNet.Jetstream;
using Xunit;

namespace CarpaNet.UnitTests.Jetstream;

/// <summary>
/// Frame fixtures mirror the reference Go client's livedecode tests: the canonical
/// six-fractional-digit wire time, the proposal-0015 envelope, and the same
/// malformed-vs-skip classification.
/// </summary>
public class JetstreamV2FrameDecoderTests
{
    private const string WireTime = "1970-01-01T00:00:00.000001Z";

    private static JetstreamV2FrameResult Decode(string frame) =>
        JetstreamV2FrameDecoder.Decode(Encoding.UTF8.GetBytes(frame));

    private static string CommitFrame(long seq, string did, string op, string collection, string rkey, bool withRecord)
    {
        var frame = "{\"$type\":\"message\",\"payload\":{\"$type\":\"network.bsky.jetstream.subscribeEvents#commit\"" +
            $",\"seq\":{seq},\"did\":\"{did}\",\"time\":\"{WireTime}\"" +
            $",\"rev\":\"r\",\"operation\":\"{op}\",\"collection\":\"{collection}\",\"rkey\":\"{rkey}\"";
        if (withRecord)
        {
            frame += $",\"cid\":\"bafytest\",\"record\":{{\"$type\":\"{collection}\",\"text\":\"hi\"}}";
        }

        return frame + "}}";
    }

    [Fact]
    public void Decode_CommitCreate_ProducesEvent()
    {
        var result = Decode(CommitFrame(42, "did:plc:a", "create", "app.bsky.feed.post", "r1", withRecord: true));

        Assert.Equal(JetstreamV2FrameKind.Event, result.Kind);
        var evt = result.Event!;
        Assert.Equal(JetstreamV2EventKind.Commit, evt.Kind);
        Assert.Equal(42, evt.Seq);
        Assert.Equal("did:plc:a", evt.Did);
        Assert.Equal(1, evt.TimeUs); // the canonical datetime parses back to unix microseconds
        Assert.Equal(JetstreamV2CommitOperation.Create, evt.Commit!.Operation);
        Assert.Equal("app.bsky.feed.post", evt.Commit.Collection);
        Assert.Equal("r1", evt.Commit.Rkey);
        Assert.Equal("bafytest", evt.Commit.Cid);
        Assert.Equal("hi", evt.Commit.Record!.Value.GetProperty("text").GetString());
    }

    [Fact]
    public void Decode_CommitDelete_HasNoRecordOrCid()
    {
        var result = Decode(CommitFrame(7, "did:plc:a", "delete", "app.bsky.feed.post", "r1", withRecord: false));

        Assert.Equal(JetstreamV2FrameKind.Event, result.Kind);
        Assert.Equal(JetstreamV2CommitOperation.Delete, result.Event!.Commit!.Operation);
        Assert.Null(result.Event.Commit.Record);
        Assert.Null(result.Event.Commit.Cid);
    }

    [Fact]
    public void Decode_CreateMissingRecord_IsMalformed()
    {
        var result = Decode(CommitFrame(1, "did:plc:a", "create", "app.bsky.feed.post", "r", withRecord: false));

        Assert.Equal(JetstreamV2FrameKind.Malformed, result.Kind);
        Assert.Contains("missing record", result.Name);
    }

    [Fact]
    public void Decode_UnknownOperation_IsMalformed()
    {
        var result = Decode(CommitFrame(1, "did:plc:a", "upsert", "app.bsky.feed.post", "r", withRecord: true));

        Assert.Equal(JetstreamV2FrameKind.Malformed, result.Kind);
    }

    [Fact]
    public void Decode_InvalidSeq_IsMalformed()
    {
        // Seqs are 1-based on the wire; 0 means the required field was absent. Accepting it
        // would hand the session an event the dedup silently swallows.
        var result = Decode(CommitFrame(0, "did:plc:a", "create", "app.bsky.feed.post", "r", withRecord: true));

        Assert.Equal(JetstreamV2FrameKind.Malformed, result.Kind);
        Assert.Contains("invalid seq", result.Name);
    }

    [Fact]
    public void Decode_Identity_WrapsUpstreamEvent()
    {
        var frame = "{\"$type\":\"message\",\"payload\":{\"$type\":\"network.bsky.jetstream.subscribeEvents#identity\"" +
            $",\"seq\":6,\"did\":\"did:plc:a\",\"time\":\"{WireTime}\"" +
            ",\"identity\":{\"did\":\"did:plc:a\",\"handle\":\"alice.test\",\"seq\":600,\"time\":\"2026-05-25T00:00:00Z\"}}}";
        var result = Decode(frame);

        Assert.Equal(JetstreamV2FrameKind.Event, result.Kind);
        var evt = result.Event!;
        Assert.Equal(JetstreamV2EventKind.Identity, evt.Kind);
        Assert.Equal(6, evt.Seq);
        Assert.Equal("alice.test", evt.Identity!.Handle);
        Assert.Equal(600, evt.Identity.Seq); // wrapped upstream event keeps the relay's seq
    }

    [Fact]
    public void Decode_AccountTombstone_KeepsStatus()
    {
        var frame = "{\"$type\":\"message\",\"payload\":{\"$type\":\"network.bsky.jetstream.subscribeEvents#account\"" +
            $",\"seq\":5,\"did\":\"did:plc:a\",\"time\":\"{WireTime}\"" +
            ",\"account\":{\"did\":\"did:plc:a\",\"active\":false,\"status\":\"deleted\",\"seq\":500,\"time\":\"2026-05-25T00:00:00Z\"}}}";
        var result = Decode(frame);

        Assert.Equal(JetstreamV2FrameKind.Event, result.Kind);
        var evt = result.Event!;
        Assert.Equal(JetstreamV2EventKind.Account, evt.Kind);
        Assert.False(evt.Account!.Active);
        Assert.Equal("deleted", evt.Account.Status);
        Assert.Equal(500, evt.Account.Seq);
    }

    [Fact]
    public void Decode_Sync_UsesEnvelopeSeqAndDropsBlocks()
    {
        var frame = "{\"$type\":\"message\",\"payload\":{\"$type\":\"network.bsky.jetstream.subscribeEvents#sync\"" +
            $",\"seq\":8,\"did\":\"did:plc:a\",\"time\":\"{WireTime}\"" +
            ",\"sync\":{\"did\":\"did:plc:a\",\"rev\":\"rev1\",\"seq\":800,\"time\":\"2026-05-25T00:00:00Z\",\"blocks\":{\"$bytes\":\"AQI\"}}}}";
        var result = Decode(frame);

        Assert.Equal(JetstreamV2FrameKind.Event, result.Kind);
        var evt = result.Event!;
        Assert.Equal(JetstreamV2EventKind.Sync, evt.Kind);
        Assert.Equal(8, evt.Seq); // envelope seq is Jetstream's
        Assert.Equal("rev1", evt.Sync!.Rev);
        Assert.Equal(800, evt.Sync.Seq); // wrapped upstream event keeps the relay's seq
    }

    [Fact]
    public void Decode_InfoFrame_IsAdvisory()
    {
        var frame = "{\"$type\":\"message\",\"payload\":{\"$type\":\"network.bsky.jetstream.subscribeEvents#info\"" +
            ",\"name\":\"OutdatedCursor\",\"message\":\"resumed from seq 5\"}}";
        var result = Decode(frame);

        Assert.Equal(JetstreamV2FrameKind.Info, result.Kind);
        Assert.Equal("OutdatedCursor", result.Name);
        Assert.Equal("resumed from seq 5", result.Message);
    }

    [Fact]
    public void Decode_ErrorFrame_IsTerminal()
    {
        var result = Decode("{\"$type\":\"error\",\"error\":\"ConsumerTooSlow\",\"message\":\"reconnect at cursor 9\"}");

        Assert.Equal(JetstreamV2FrameKind.StreamError, result.Kind);
        Assert.Equal("ConsumerTooSlow", result.Name);
        Assert.Equal("reconnect at cursor 9", result.Message);
    }

    [Fact]
    public void Decode_ErrorFrameWithoutCode_IsMalformed()
    {
        var result = Decode("{\"$type\":\"error\",\"message\":\"oops\"}");

        Assert.Equal(JetstreamV2FrameKind.Malformed, result.Kind);
    }

    [Fact]
    public void Decode_UnknownPayloadType_Skips()
    {
        // A newer server's message kind must not break an old client.
        var frame = "{\"$type\":\"message\",\"payload\":{\"$type\":\"network.bsky.jetstream.subscribeEvents#future\",\"seq\":1}}";
        var result = Decode(frame);

        Assert.Equal(JetstreamV2FrameKind.Skip, result.Kind);
    }

    [Fact]
    public void Decode_UnknownEnvelopeType_Skips()
    {
        var result = Decode("{\"$type\":\"heartbeat\"}");

        Assert.Equal(JetstreamV2FrameKind.Skip, result.Kind);
    }

    [Fact]
    public void Decode_MissingEnvelopeType_IsMalformed()
    {
        // No $type at all means the client hit a v1 /subscribe endpoint; skipping would make
        // the wrong endpoint look healthy while delivering nothing.
        var result = Decode("{\"did\":\"did:plc:a\",\"time_us\":1,\"kind\":\"commit\"}");

        Assert.Equal(JetstreamV2FrameKind.Malformed, result.Kind);
        Assert.Contains("subscribeEvents", result.Name);
    }

    [Fact]
    public void Decode_PayloadMissingType_IsMalformed()
    {
        // A payload with no $type is malformed, not a future addition — skipping it would be
        // silent event loss.
        var result = Decode("{\"$type\":\"message\",\"payload\":{\"seq\":1,\"did\":\"did:plc:a\"}}");

        Assert.Equal(JetstreamV2FrameKind.Malformed, result.Kind);
    }

    [Fact]
    public void Decode_InvalidJson_IsMalformed()
    {
        var result = Decode("{nope");

        Assert.Equal(JetstreamV2FrameKind.Malformed, result.Kind);
    }

    [Fact]
    public void GetRecord_DeserializesTypedRecord()
    {
        var result = Decode(CommitFrame(42, "did:plc:a", "create", "app.bsky.feed.post", "r1", withRecord: true));
        var record = result.Event!.Commit!.GetRecord(JetstreamV2TestRecordContext.Default.JetstreamV2TestRecord);

        Assert.NotNull(record);
        Assert.Equal("hi", record!.Text);
    }
}

public sealed class JetstreamV2TestRecord
{
    [System.Text.Json.Serialization.JsonPropertyName("text")]
    public string? Text { get; set; }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(JetstreamV2TestRecord))]
public partial class JetstreamV2TestRecordContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
