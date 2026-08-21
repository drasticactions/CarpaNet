namespace CarpaNet.Jetstream;

/// <summary>
/// The kind of mutation a <see cref="JetstreamV2Commit"/> carries.
/// </summary>
public enum JetstreamV2CommitOperation
{
    /// <summary>A record was created (includes resync replacement records).</summary>
    Create,

    /// <summary>A record was updated.</summary>
    Update,

    /// <summary>A record was deleted. <see cref="JetstreamV2Commit.Record"/> and <see cref="JetstreamV2Commit.Cid"/> are null.</summary>
    Delete,
}
