using System;
using CarpaNet.OAuth.Scopes;
using Xunit;

namespace CarpaNet.UnitTests.OAuth.Scopes;

/// <summary>
/// Ported from oauth-scopes scopes/blob-permission.test.ts.
/// </summary>
public class BlobPermissionTests
{
    [Fact]
    public void Parse_Positional()
    {
        Assert.Equal(new[] { "image/png" }, BlobPermission.Parse("blob:image/png").Accept);
    }

    [Fact]
    public void Parse_MultipleAccept()
    {
        Assert.Equal(
            new[] { "image/png", "image/jpeg" },
            BlobPermission.Parse("blob?accept=image/png&accept=image/jpeg").Accept);
    }

    [Theory]
    [InlineData("blob")]
    [InlineData("invalid")]
    [InlineData("scope")]
    [InlineData("blob:invalid")]
    [InlineData("blob?accept=invalid-mime")]
    [InlineData("blob?accept=invalid")]
    [InlineData("blob:*/**")]
    [InlineData("blob:*/png")]
    [InlineData("blob:image/png?accept=image/jpeg")]
    [InlineData("blob:image/png?mime=image/jpeg")]
    public void Parse_Invalid_ReturnsFalse(string scope)
    {
        Assert.False(BlobPermission.TryParse(scope, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void ScopeNeededFor()
    {
        Assert.Equal("blob:image/png", BlobPermission.ScopeNeededFor("image/png"));
        Assert.Equal("blob:application/json", BlobPermission.ScopeNeededFor("application/json"));
    }

    [Fact]
    public void Matches()
    {
        Assert.True(BlobPermission.Parse("blob:image/png").Matches("image/png"));
        Assert.False(BlobPermission.Parse("blob:image/png").Matches("image/jpeg"));

        var any = BlobPermission.Parse("blob:*/*");
        Assert.True(any.Matches("image/jpeg"));
        Assert.True(any.Matches("application/json"));

        Assert.True(BlobPermission.Parse("blob:image/*").Matches("image/gif"));

        var multiple = BlobPermission.Parse("blob?accept=image/png&accept=image/jpeg");
        Assert.True(multiple.Matches("image/png"));
        Assert.True(multiple.Matches("image/jpeg"));
        Assert.False(multiple.Matches("image/gif"));
    }

    [Fact]
    public void Format_MultipleAccept_UsesSortedQuery()
    {
        Assert.Equal("blob?accept=image/jpeg&accept=image/png", new BlobPermission("image/png", "image/jpeg").ToString());
    }

    [Fact]
    public void Format_StripsRedundantAccept()
    {
        Assert.Equal("blob:*/*", new BlobPermission("*/*", "image/*").ToString());
        Assert.Equal("blob:*/*", new BlobPermission("*/*", "image/png").ToString());
        Assert.Equal("blob:image/*", new BlobPermission("image/*", "image/png").ToString());
    }

    [Fact]
    public void Format_SingleAccept_UsesPositional()
    {
        Assert.Equal("blob:image/png", new BlobPermission("image/png").ToString());
        Assert.Equal("blob:image/*", new BlobPermission("image/*").ToString());
        Assert.Equal("blob:*/*", new BlobPermission("*/*").ToString());
        Assert.Equal("blob:image/png", new BlobPermission("IMAGE/PNG").ToString());
    }

    [Fact]
    public void Constructor_RejectsInvalidAccept()
    {
        Assert.Throws<ArgumentException>(() => new BlobPermission());
        Assert.Throws<ArgumentException>(() => new BlobPermission("image"));
        Assert.Throws<ArgumentException>(() => new BlobPermission("*/png"));
    }
}
