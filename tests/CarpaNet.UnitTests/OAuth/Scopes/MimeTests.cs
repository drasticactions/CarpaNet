using CarpaNet.OAuth.Scopes;
using Xunit;

namespace CarpaNet.UnitTests.OAuth.Scopes;

/// <summary>
/// Ported from oauth-scopes lib/mime.test.ts.
/// </summary>
public class MimeTests
{
    [Theory]
    [InlineData("image/png", true)]
    [InlineData("application/json", true)]
    [InlineData("text/html", true)]
    [InlineData("image/*", true)]
    [InlineData("*/*", true)]
    [InlineData("image//png", false)]
    [InlineData("/png", false)]
    [InlineData("image/", false)]
    [InlineData("image/**", false)]
    [InlineData("*/png", false)]
    [InlineData("*", false)]
    [InlineData("image/png/extra", false)]
    public void IsAccept(string value, bool expected)
    {
        Assert.Equal(expected, BlobPermission.IsAccept(value));
    }

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("application/json", true)]
    [InlineData("image/*", false)]
    [InlineData("*/*", false)]
    [InlineData("image/png/extra", false)]
    [InlineData("*/mime", false)]
    [InlineData("/png", false)]
    [InlineData("image/", false)]
    [InlineData("image", false)]
    [InlineData("image/ png", false)]
    [InlineData("image//png", false)]
    public void IsMime(string value, bool expected)
    {
        Assert.Equal(expected, BlobPermission.IsMime(value));
    }

    [Theory]
    [InlineData("image/png", "image/png", true)]
    [InlineData("image/*", "image/jpeg", true)]
    [InlineData("image/*", "image/gif", true)]
    [InlineData("image/png", "image/jpeg", false)]
    [InlineData("image/*", "text/html", false)]
    [InlineData("*/*", "application/json", true)]
    [InlineData("image/png", "*/mime", false)]
    [InlineData("image/png", "image", false)]
    [InlineData("image/*", "image//png", false)]
    [InlineData("image/*", "image/ png", false)]
    [InlineData("*/*", "image/", false)]
    [InlineData("*/*", "/mime", false)]
    public void MatchesAccept(string accept, string mime, bool expected)
    {
        Assert.Equal(expected, BlobPermission.MatchesAccept(accept, mime));
    }

    [Fact]
    public void MatchesAnyAccept()
    {
        var accepts = new[] { "image/png", "application/json" };
        Assert.True(BlobPermission.MatchesAnyAccept(accepts, "image/png"));
        Assert.True(BlobPermission.MatchesAnyAccept(accepts, "application/json"));
        Assert.False(BlobPermission.MatchesAnyAccept(accepts, "text/html"));
        Assert.False(BlobPermission.MatchesAnyAccept(System.Array.Empty<string>(), "image/png"));
        Assert.True(BlobPermission.MatchesAnyAccept(new[] { "image/*" }, "image/jpeg"));
        Assert.False(BlobPermission.MatchesAnyAccept(new[] { "image/*" }, "text/html"));
    }
}
