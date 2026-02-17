using CarpaNet.BuildTasks;
using Xunit;

namespace CarpaNet.UnitTests.BuildTasks;

public class NsidAuthorityTests
{
    [Theory]
    [InlineData("com.example.myapp.getProfile", true)]
    [InlineData("app.bsky.feed.getTimeline", true)]
    [InlineData("com.atproto.repo.createRecord", true)]
    [InlineData("tools.ozone.moderation.defs", true)]
    [InlineData("chat.bsky.convo.getConvo", true)]
    [InlineData("com.example", false)]           // Only 2 segments
    [InlineData("invalid", false)]                // Only 1 segment
    [InlineData("", false)]                       // Empty
    [InlineData("1com.example.foo", false)]       // First segment starts with digit
    [InlineData("com..example.foo", false)]       // Empty segment
    [InlineData("com.example.foo-", false)]       // Segment ends with hyphen
    public void IsValidNsid_ValidatesCorrectly(string nsid, bool expected)
    {
        Assert.Equal(expected, NsidAuthority.IsValidNsid(nsid));
    }

    [Theory]
    [InlineData("com.example.myapp.getProfile", "com.example.myapp")]
    [InlineData("app.bsky.feed.getTimeline", "app.bsky.feed")]
    [InlineData("com.atproto.repo.createRecord", "com.atproto.repo")]
    [InlineData("tools.ozone.moderation.defs", "tools.ozone.moderation")]
    public void GetAuthority_ExtractsCorrectly(string nsid, string expected)
    {
        Assert.Equal(expected, NsidAuthority.GetAuthority(nsid));
    }

    [Theory]
    [InlineData("com.example.myapp", "_lexicon.myapp.example.com")]
    [InlineData("app.bsky.feed", "_lexicon.feed.bsky.app")]
    [InlineData("com.atproto.repo", "_lexicon.repo.atproto.com")]
    public void AuthorityToDnsName_ConvertsCorrectly(string authority, string expected)
    {
        Assert.Equal(expected, NsidAuthority.AuthorityToDnsName(authority));
    }

    [Theory]
    [InlineData("com.example.myapp.getProfile", "_lexicon.myapp.example.com")]
    [InlineData("app.bsky.feed.getTimeline", "_lexicon.feed.bsky.app")]
    [InlineData("com.atproto.repo.createRecord", "_lexicon.repo.atproto.com")]
    public void NsidToDnsName_ConvertsCorrectly(string nsid, string expected)
    {
        Assert.Equal(expected, NsidAuthority.NsidToDnsName(nsid));
    }

    [Fact]
    public void GetAuthority_ThrowsForInvalidFormat()
    {
        Assert.Throws<ArgumentException>(() => NsidAuthority.GetAuthority("noDotsHere"));
    }
}
