using CarpaNet.Utilities;
using Xunit;

namespace CarpaNet.UnitTests;

public class NsidHelperTests
{
    [Theory]
    [InlineData("app.bsky.feed.post", "AppBsky.Feed")]
    [InlineData("com.atproto.repo.createRecord", "ComAtproto.Repo")]
    [InlineData("app.bsky.actor.defs", "AppBsky.Actor")]
    [InlineData("tools.ozone.moderation.defs", "ToolsOzone.Moderation")]
    [InlineData("chat.bsky.convo.getConvo", "ChatBsky.Convo")]
    public void ToNamespace_ConvertsNsidToNamespace(string nsid, string expected)
    {
        var result = NsidHelper.ToNamespace(nsid);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("app.bsky.feed.post", "MyApp", "MyApp.AppBsky.Feed")]
    [InlineData("com.atproto.repo.createRecord", "MyApp", "MyApp.ComAtproto.Repo")]
    public void ToNamespace_WithRootNamespace_PrependsRoot(string nsid, string root, string expected)
    {
        var result = NsidHelper.ToNamespace(nsid, root);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("app.bsky.feed.post", "Post")]
    [InlineData("com.atproto.repo.createRecord", "CreateRecord")]
    [InlineData("app.bsky.actor.defs", "Defs")]
    public void ToTypeName_ExtractsTypeName(string nsid, string expected)
    {
        var result = NsidHelper.ToTypeName(nsid);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("app.bsky.feed.defs#feedViewPost", "FeedViewPost")]
    [InlineData("#localType", "LocalType")]
    [InlineData("app.bsky.feed.post", "Post")]
    public void RefToTypeName_ExtractsTypeNameFromRef(string refString, string expected)
    {
        var result = NsidHelper.RefToTypeName(refString);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("#localType", "app.bsky.feed.post", "AppBsky.Feed")]
    [InlineData("app.bsky.actor.defs#profileView", "app.bsky.feed.post", "AppBsky.Actor")]
    [InlineData("com.atproto.repo.strongRef", "app.bsky.feed.post", "ComAtproto.Repo")]
    public void RefToNamespace_ResolvesNamespaceFromRef(string refString, string currentNsid, string expected)
    {
        var result = NsidHelper.RefToNamespace(refString, currentNsid);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("hello", "Hello")]
    [InlineData("hello-world", "HelloWorld")]
    [InlineData("hello_world", "HelloWorld")]
    [InlineData("helloWorld", "HelloWorld")]
    [InlineData("HELLO", "HELLO")]
    public void ToPascalCase_ConvertsToPascalCase(string input, string expected)
    {
        var result = NsidHelper.ToPascalCase(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Hello", "hello")]
    [InlineData("HelloWorld", "helloWorld")]
    [InlineData("ABC", "aBC")]
    public void ToCamelCase_ConvertsToCamelCase(string input, string expected)
    {
        var result = NsidHelper.ToCamelCase(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("event", "@event")]
    [InlineData("record", "@record")]
    [InlineData("class", "@class")]
    [InlineData("Event", "@Event")]
    [InlineData("validName", "validName")]
    [InlineData("MyClass", "MyClass")]
    public void EscapeIdentifier_EscapesCSharpKeywords(string input, string expected)
    {
        var result = NsidHelper.EscapeIdentifier(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("@event", "event")]
    [InlineData("@record", "record")]
    [InlineData("validName", "validName")]
    public void StripEscapePrefix_RemovesAtPrefix(string input, string expected)
    {
        var result = NsidHelper.StripEscapePrefix(input);
        Assert.Equal(expected, result);
    }
}
