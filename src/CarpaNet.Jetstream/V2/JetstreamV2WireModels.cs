using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarpaNet.Jetstream;

/// <summary>
/// The xrpc.v1.json frame envelope (atproto proposal 0015): exactly one self-describing
/// object per text frame, discriminated by $type ("message" or "error").
/// </summary>
internal sealed class JetstreamV2Envelope
{
    [JsonPropertyName("$type")]
    public string? Type { get; set; }

    /// <summary>Message frames: the payload union, dispatched on its own $type.</summary>
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    /// <summary>Error frames: the bare error type name.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Error frames: optional description.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>network.bsky.jetstream.subscribeEvents#commit wire payload.</summary>
internal sealed class JetstreamV2CommitPayload
{
    [JsonPropertyName("seq")]
    public long Seq { get; set; }

    [JsonPropertyName("did")]
    public string? Did { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("rev")]
    public string? Rev { get; set; }

    [JsonPropertyName("operation")]
    public string? Operation { get; set; }

    [JsonPropertyName("collection")]
    public string? Collection { get; set; }

    [JsonPropertyName("rkey")]
    public string? Rkey { get; set; }

    [JsonPropertyName("cid")]
    public string? Cid { get; set; }

    [JsonPropertyName("record")]
    public JsonElement? Record { get; set; }
}

/// <summary>The upstream com.atproto.sync.subscribeRepos#identity event, wrapped verbatim.</summary>
internal sealed class JetstreamV2UpstreamIdentity
{
    [JsonPropertyName("did")]
    public string? Did { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("seq")]
    public long Seq { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }
}

/// <summary>network.bsky.jetstream.subscribeEvents#identity wire payload.</summary>
internal sealed class JetstreamV2IdentityPayload
{
    [JsonPropertyName("seq")]
    public long Seq { get; set; }

    [JsonPropertyName("did")]
    public string? Did { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("identity")]
    public JetstreamV2UpstreamIdentity? Identity { get; set; }
}

/// <summary>The upstream com.atproto.sync.subscribeRepos#account event, wrapped verbatim.</summary>
internal sealed class JetstreamV2UpstreamAccount
{
    [JsonPropertyName("did")]
    public string? Did { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("seq")]
    public long Seq { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }
}

/// <summary>network.bsky.jetstream.subscribeEvents#account wire payload.</summary>
internal sealed class JetstreamV2AccountPayload
{
    [JsonPropertyName("seq")]
    public long Seq { get; set; }

    [JsonPropertyName("did")]
    public string? Did { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("account")]
    public JetstreamV2UpstreamAccount? Account { get; set; }
}

/// <summary>The upstream com.atproto.sync.subscribeRepos#sync event, wrapped verbatim.
/// The raw MST block bytes are deliberately not surfaced.</summary>
internal sealed class JetstreamV2UpstreamSync
{
    [JsonPropertyName("did")]
    public string? Did { get; set; }

    [JsonPropertyName("rev")]
    public string? Rev { get; set; }

    [JsonPropertyName("seq")]
    public long Seq { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }
}

/// <summary>network.bsky.jetstream.subscribeEvents#sync wire payload.</summary>
internal sealed class JetstreamV2SyncPayload
{
    [JsonPropertyName("seq")]
    public long Seq { get; set; }

    [JsonPropertyName("did")]
    public string? Did { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("sync")]
    public JetstreamV2UpstreamSync? Sync { get; set; }
}

/// <summary>network.bsky.jetstream.subscribeEvents#info advisory payload (no seq, not an event).</summary>
internal sealed class JetstreamV2InfoPayload
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>The standard XRPC JSON error envelope returned on pre-upgrade rejections.</summary>
internal sealed class JetstreamV2XrpcError
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>network.bsky.jetstream.planSnapshot input body.</summary>
internal sealed class JetstreamV2PlanSnapshotInput
{
    [JsonPropertyName("kinds")]
    public List<string>? Kinds { get; set; }

    [JsonPropertyName("dids")]
    public List<string>? Dids { get; set; }

    [JsonPropertyName("collections")]
    public List<string>? Collections { get; set; }

    [JsonPropertyName("afterSeq")]
    public long? AfterSeq { get; set; }

    [JsonPropertyName("beforeSeq")]
    public long? BeforeSeq { get; set; }
}

/// <summary>network.bsky.jetstream.planSnapshot output body.</summary>
internal sealed class JetstreamV2PlanSnapshotOutput
{
    [JsonPropertyName("plannedThroughSeq")]
    public long PlannedThroughSeq { get; set; }

    [JsonPropertyName("sealedTipSeq")]
    public long SealedTipSeq { get; set; }

    [JsonPropertyName("segments")]
    public List<JetstreamV2PlanSegmentDto>? Segments { get; set; }
}

/// <summary>One planned segment in a planSnapshot response.</summary>
internal sealed class JetstreamV2PlanSegmentDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("index")]
    public long Index { get; set; }

    [JsonPropertyName("checksum")]
    public string? Checksum { get; set; }

    [JsonPropertyName("minSeq")]
    public long MinSeq { get; set; }

    [JsonPropertyName("maxSeq")]
    public long MaxSeq { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("blocks")]
    public List<JetstreamV2PlanBlockRangeDto>? Blocks { get; set; }
}

/// <summary>One inclusive block range in a planned blocks-mode segment.</summary>
internal sealed class JetstreamV2PlanBlockRangeDto
{
    [JsonPropertyName("first")]
    public long First { get; set; }

    [JsonPropertyName("last")]
    public long Last { get; set; }
}

/// <summary>
/// Source-generated JSON context for the Jetstream v2 wire types.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JetstreamV2Envelope))]
[JsonSerializable(typeof(JetstreamV2CommitPayload))]
[JsonSerializable(typeof(JetstreamV2IdentityPayload))]
[JsonSerializable(typeof(JetstreamV2AccountPayload))]
[JsonSerializable(typeof(JetstreamV2SyncPayload))]
[JsonSerializable(typeof(JetstreamV2InfoPayload))]
[JsonSerializable(typeof(JetstreamV2XrpcError))]
[JsonSerializable(typeof(JetstreamV2PlanSnapshotInput))]
[JsonSerializable(typeof(JetstreamV2PlanSnapshotOutput))]
internal partial class JetstreamV2JsonContext : JsonSerializerContext
{
}
