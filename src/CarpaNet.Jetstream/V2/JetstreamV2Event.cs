using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CarpaNet.Jetstream;

/// <summary>
/// A single decoded Jetstream v2 event.
/// </summary>
public sealed class JetstreamV2Event
{
    /// <summary>
    /// The repository (account) DID this event belongs to.
    /// </summary>
    public string Did { get; set; } = string.Empty;

    /// <summary>
    /// Jetstream's monotonic per-event sequence number (the stream cursor). Persist the last
    /// seen value to resume later via <see cref="JetstreamV2SubscribeOptions.LiveCursor"/> or
    /// <see cref="JetstreamV2SubscribeOptions.AfterSeq"/>. Cursors are instance-local: seq
    /// values do not transfer between Jetstream servers.
    /// </summary>
    public long Seq { get; set; }

    /// <summary>
    /// The event's display timestamp in microseconds since the Unix epoch: the time Jetstream
    /// witnessed the event, unless an operator timestamp import overrode it. It is not the
    /// record's client-supplied createdAt. <see cref="Seq"/> is the only faithful resume position.
    /// </summary>
    public long TimeUs { get; set; }

    /// <summary>
    /// Selects which of the payload properties below is populated.
    /// </summary>
    public JetstreamV2EventKind Kind { get; set; }

    /// <summary>
    /// The commit payload when <see cref="Kind"/> is <see cref="JetstreamV2EventKind.Commit"/>.
    /// </summary>
    public JetstreamV2Commit? Commit { get; set; }

    /// <summary>
    /// The identity payload when <see cref="Kind"/> is <see cref="JetstreamV2EventKind.Identity"/>.
    /// </summary>
    public JetstreamV2Identity? Identity { get; set; }

    /// <summary>
    /// The account payload when <see cref="Kind"/> is <see cref="JetstreamV2EventKind.Account"/>.
    /// </summary>
    public JetstreamV2Account? Account { get; set; }

    /// <summary>
    /// The sync payload when <see cref="Kind"/> is <see cref="JetstreamV2EventKind.Sync"/>.
    /// </summary>
    public JetstreamV2Sync? Sync { get; set; }
}
