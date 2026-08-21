using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using CarpaNet.Jetstream;
using Xunit;

namespace CarpaNet.UnitTests.Jetstream;

public class JetstreamV2SubscribeOptionsTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        new JetstreamV2SubscribeOptions().Validate();
    }

    [Fact]
    public void BeforeSeq_RequiresSnapshotOnly()
    {
        var options = new JetstreamV2SubscribeOptions { BeforeSeq = 100 };
        Assert.Throws<ArgumentException>(() => options.Validate());

        options.SnapshotOnly = true;
        options.Validate();
    }

    [Fact]
    public void SnapshotOnly_RequiresAReplayBound()
    {
        var options = new JetstreamV2SubscribeOptions { SnapshotOnly = true };
        Assert.Throws<ArgumentException>(() => options.Validate());

        options.AfterSeq = 0;
        options.Validate();
    }

    [Fact]
    public void NegativeSeqs_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new JetstreamV2SubscribeOptions { AfterSeq = -1 }.Validate());
        Assert.Throws<ArgumentException>(() => new JetstreamV2SubscribeOptions { LiveCursor = -1 }.Validate());
    }

    [Fact]
    public void FilterLimits_AreEnforced()
    {
        var tooManyCollections = new JetstreamV2SubscribeOptions
        {
            Collections = Enumerable.Range(0, 101).Select(i => $"c.o.l{i}").ToList(),
        };
        Assert.Throws<ArgumentException>(() => tooManyCollections.Validate());

        var tooManyKinds = new JetstreamV2SubscribeOptions
        {
            Kinds = new[]
            {
                JetstreamV2EventKind.Commit, JetstreamV2EventKind.Identity, JetstreamV2EventKind.Account,
                JetstreamV2EventKind.Sync, JetstreamV2EventKind.Commit,
            },
        };
        Assert.Throws<ArgumentException>(() => tooManyKinds.Validate());
    }

    [Fact]
    public void BareWildcards_AreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new JetstreamV2SubscribeOptions { Collections = new[] { "*" } }.Validate());
        Assert.Throws<ArgumentException>(() =>
            new JetstreamV2SubscribeOptions { Collections = new[] { ".*" } }.Validate());

        new JetstreamV2SubscribeOptions { Collections = new[] { "app.bsky.feed.*" } }.Validate();
    }

    [Fact]
    public void BackfillRequested_TracksSeqBounds()
    {
        Assert.False(new JetstreamV2SubscribeOptions().BackfillRequested);
        Assert.True(new JetstreamV2SubscribeOptions { AfterSeq = 0 }.BackfillRequested);
        Assert.True(new JetstreamV2SubscribeOptions { BeforeSeq = 5, SnapshotOnly = true }.BackfillRequested);
    }

    [Fact]
    public void KindStrings_MapAndDeduplicate()
    {
        var strings = JetstreamV2Engine.KindStrings(new[]
        {
            JetstreamV2EventKind.Commit, JetstreamV2EventKind.Sync, JetstreamV2EventKind.Commit,
        });

        Assert.Equal(new[] { "commit", "sync" }, strings);
    }
}

public class JetstreamZstdDictionaryTests
{
    [Fact]
    public void TryParseId_ReadsStructuredHeader()
    {
        var blob = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(blob, 0xEC30A437);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(4), 20260811);

        Assert.True(JetstreamZstdDictionary.TryParseId(blob, out var id));
        Assert.Equal(20260811u, id);
    }

    [Fact]
    public void TryParseId_RejectsShortMissingMagicAndZeroId()
    {
        Assert.False(JetstreamZstdDictionary.TryParseId(new byte[4], out _));
        Assert.False(JetstreamZstdDictionary.TryParseId(null, out _));

        var noMagic = new byte[16];
        Assert.False(JetstreamZstdDictionary.TryParseId(noMagic, out _));

        var zeroId = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(zeroId, 0xEC30A437);
        Assert.False(JetstreamZstdDictionary.TryParseId(zeroId, out _));
    }
}

public class JetstreamV2PlanConverterTests
{
    private static JetstreamV2PlanSegmentDto ValidSegment() => new()
    {
        Name = "seg_000000002a.jss",
        Index = 42,
        Checksum = "0123456789abcdef",
        MinSeq = 100,
        MaxSeq = 200,
        Mode = "segment",
    };

    [Fact]
    public void Convert_WholeSegmentPlan()
    {
        var plan = JetstreamV2PlanConverter.Convert(new JetstreamV2PlanSnapshotOutput
        {
            PlannedThroughSeq = 200,
            SealedTipSeq = 500,
            Segments = new List<JetstreamV2PlanSegmentDto> { ValidSegment() },
        });

        Assert.Equal(200, plan.PlannedThroughSeq);
        Assert.Equal(500, plan.SealedTipSeq);
        var segment = Assert.Single(plan.Segments);
        Assert.Equal(JetstreamSegmentPlanMode.WholeSegment, segment.Mode);
        Assert.Equal("seg_000000002a.jss", segment.Name);
    }

    [Fact]
    public void Convert_BlocksModeRequiresRanges()
    {
        var dto = ValidSegment();
        dto.Mode = "blocks";
        var output = new JetstreamV2PlanSnapshotOutput
        {
            PlannedThroughSeq = 200,
            SealedTipSeq = 500,
            Segments = new List<JetstreamV2PlanSegmentDto> { dto },
        };

        Assert.Throws<JetstreamV2Exception>(() => JetstreamV2PlanConverter.Convert(output));

        dto.Blocks = new List<JetstreamV2PlanBlockRangeDto> { new() { First = 2, Last = 5 } };
        var plan = JetstreamV2PlanConverter.Convert(output);
        var range = Assert.Single(plan.Segments[0].Blocks);
        Assert.Equal(2, range.First);
        Assert.Equal(5, range.Last);
    }

    [Fact]
    public void Convert_RejectsInvalidResponses()
    {
        Assert.Throws<JetstreamV2Exception>(() => JetstreamV2PlanConverter.Convert(
            new JetstreamV2PlanSnapshotOutput { PlannedThroughSeq = 10, SealedTipSeq = 5 }));

        Assert.Throws<JetstreamV2Exception>(() => JetstreamV2PlanConverter.Convert(
            new JetstreamV2PlanSnapshotOutput { PlannedThroughSeq = -1, SealedTipSeq = 5 }));

        var inverted = ValidSegment();
        inverted.MinSeq = 300;
        Assert.Throws<JetstreamV2Exception>(() => JetstreamV2PlanConverter.Convert(
            new JetstreamV2PlanSnapshotOutput
            {
                PlannedThroughSeq = 200,
                SealedTipSeq = 500,
                Segments = new List<JetstreamV2PlanSegmentDto> { inverted },
            }));

        var unknownMode = ValidSegment();
        unknownMode.Mode = "streaming";
        Assert.Throws<JetstreamV2Exception>(() => JetstreamV2PlanConverter.Convert(
            new JetstreamV2PlanSnapshotOutput
            {
                PlannedThroughSeq = 200,
                SealedTipSeq = 500,
                Segments = new List<JetstreamV2PlanSegmentDto> { unknownMode },
            }));
    }
}

public class JetstreamV2TransportUriTests
{
    [Fact]
    public void BuildXrpcUri_EncodesRepeatedParameters()
    {
        var uri = JetstreamV2Transport.BuildXrpcUri(
            new Uri("https://jetstream.example"),
            "network.bsky.jetstream.getBlock",
            new[]
            {
                new KeyValuePair<string, string>("segment", "seg_000000002a.jss"),
                new KeyValuePair<string, string>("blockIndex", "7"),
            });

        Assert.Equal("https://jetstream.example/xrpc/network.bsky.jetstream.getBlock?segment=seg_000000002a.jss&blockIndex=7", uri.ToString());
    }
}
