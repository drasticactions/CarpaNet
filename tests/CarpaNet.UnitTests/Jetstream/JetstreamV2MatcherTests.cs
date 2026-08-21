using CarpaNet.Jetstream;
using Xunit;

namespace CarpaNet.UnitTests.Jetstream;

public class JetstreamV2MatcherTests
{
    private static JetstreamV2Event Commit(long seq, string did, string collection) => new()
    {
        Seq = seq,
        Did = did,
        Kind = JetstreamV2EventKind.Commit,
        Commit = new JetstreamV2Commit { Collection = collection },
    };

    private static JetstreamV2Event Account(long seq, string did) => new()
    {
        Seq = seq,
        Did = did,
        Kind = JetstreamV2EventKind.Account,
        Account = new JetstreamV2Account { Did = did, Active = false, Status = "deleted" },
    };

    [Fact]
    public void NoFilters_MatchesEverything()
    {
        var matcher = new JetstreamV2Matcher(new JetstreamV2SubscribeOptions());

        Assert.True(matcher.WantsEvent(Commit(1, "did:plc:a", "app.bsky.feed.post")));
        Assert.True(matcher.WantsEvent(Account(2, "did:plc:a")));
    }

    [Fact]
    public void CollectionFilter_ExactAndWildcard()
    {
        var matcher = new JetstreamV2Matcher(new JetstreamV2SubscribeOptions
        {
            Collections = new[] { "app.bsky.feed.post", "app.bsky.graph.*" },
        });

        Assert.True(matcher.WantsEvent(Commit(1, "did:plc:a", "app.bsky.feed.post")));
        Assert.True(matcher.WantsEvent(Commit(2, "did:plc:a", "app.bsky.graph.follow")));
        Assert.False(matcher.WantsEvent(Commit(3, "did:plc:a", "app.bsky.feed.like")));
        // The wildcard keeps the dot: "app.bsky.graphx" must not match "app.bsky.graph.*".
        Assert.False(matcher.WantsEvent(Commit(4, "did:plc:a", "app.bsky.graphx.follow")));
    }

    [Fact]
    public void CollectionFilter_DidLevelMarkersBypass()
    {
        // #account/#identity/#sync are the only purge signal a folding consumer gets; a
        // collection filter must never suppress them.
        var matcher = new JetstreamV2Matcher(new JetstreamV2SubscribeOptions
        {
            Collections = new[] { "app.bsky.feed.post" },
        });

        Assert.True(matcher.WantsEvent(Account(1, "did:plc:a")));
    }

    [Fact]
    public void KindsCommitOnly_ExcludesMarkers()
    {
        var matcher = new JetstreamV2Matcher(new JetstreamV2SubscribeOptions
        {
            Kinds = new[] { JetstreamV2EventKind.Commit },
        });

        Assert.True(matcher.WantsEvent(Commit(1, "did:plc:a", "app.bsky.feed.post")));
        Assert.False(matcher.WantsEvent(Account(2, "did:plc:a")));
    }

    [Fact]
    public void DidFilter_AppliesToEveryKind()
    {
        var matcher = new JetstreamV2Matcher(new JetstreamV2SubscribeOptions
        {
            Dids = new[] { "did:plc:a" },
        });

        Assert.True(matcher.WantsEvent(Commit(1, "did:plc:a", "c.o.l")));
        Assert.False(matcher.WantsEvent(Commit(2, "did:plc:b", "c.o.l")));
        Assert.False(matcher.WantsEvent(Account(3, "did:plc:b")));
    }

    [Fact]
    public void SeqWindow_IsExclusiveInclusive()
    {
        var matcher = new JetstreamV2Matcher(new JetstreamV2SubscribeOptions
        {
            AfterSeq = 10,
            BeforeSeq = 20,
            SnapshotOnly = true,
        });

        Assert.False(matcher.WantsEvent(Commit(10, "did:plc:a", "c.o.l"))); // afterSeq is exclusive
        Assert.True(matcher.WantsEvent(Commit(11, "did:plc:a", "c.o.l")));
        Assert.True(matcher.WantsEvent(Commit(20, "did:plc:a", "c.o.l"))); // beforeSeq is inclusive
        Assert.False(matcher.WantsEvent(Commit(21, "did:plc:a", "c.o.l")));
    }

    [Fact]
    public void AfterSeqZero_ImposesNoLowerBound()
    {
        var matcher = new JetstreamV2Matcher(new JetstreamV2SubscribeOptions { AfterSeq = 0 });

        Assert.True(matcher.WantsEvent(Commit(1, "did:plc:a", "c.o.l")));
    }

    [Fact]
    public void SetAfterSeq_RaisesTheFloor()
    {
        var matcher = new JetstreamV2Matcher(new JetstreamV2SubscribeOptions { AfterSeq = 0 });
        matcher.SetAfterSeq(5);

        Assert.False(matcher.WantsEvent(Commit(5, "did:plc:a", "c.o.l")));
        Assert.True(matcher.WantsEvent(Commit(6, "did:plc:a", "c.o.l")));
    }

    [Fact]
    public void WantsRow_MapsSegmentKinds()
    {
        var matcher = new JetstreamV2Matcher(new JetstreamV2SubscribeOptions
        {
            Kinds = new[] { JetstreamV2EventKind.Commit },
        });

        Assert.True(matcher.WantsRow(new JetstreamSegmentRow { Seq = 1, Kind = JetstreamSegmentRowKind.CreateResync, Did = "d" }));
        Assert.False(matcher.WantsRow(new JetstreamSegmentRow { Seq = 2, Kind = JetstreamSegmentRowKind.Identity, Did = "d" }));
    }
}
