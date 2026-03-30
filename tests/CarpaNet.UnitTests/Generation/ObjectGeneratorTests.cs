using CarpaNet.Generation;
using CarpaNet.Models;

using Xunit;

namespace CarpaNet.UnitTests.Generation;

public class ObjectGeneratorTests
{
    [Fact]
    public void RecordWithTypeProperty_RenamesPropertyToTypeValue()
    {
        // Arrange: a record type with a lexicon-defined "type" property
        // This conflicts with the auto-generated "Type" property for the $type discriminator
        var registry = new TypeRegistry();
        var def = new LexiconDefinition
        {
            Type = "object",
            Properties = new Dictionary<string, LexiconDefinition>
            {
                ["type"] = new LexiconDefinition { Type = "string" },
                ["subject"] = new LexiconDefinition { Type = "string" },
            },
            RequiredRaw = CreateJsonArray("type", "subject"),
        };

        var sb = new SourceBuilder();

        // Act
        ObjectGenerator.GenerateClass(
            sb, "Reaction", def, "tech.tokimeki.kaku.reaction", registry,
            isRecord: true, recordType: "tech.tokimeki.kaku.reaction",
            typeId: "tech.tokimeki.kaku.reaction");

        var result = sb.ToString();

        // Assert: the $type discriminator property exists
        Assert.Contains("public string Type => RecordType;", result);

        // Assert: the lexicon "type" property is renamed to TypeValue
        Assert.Contains("JsonPropertyName(\"type\")", result);
        Assert.Contains("TypeValue", result);

        // Assert: there is no duplicate "Type" settable property (only the computed "Type =>" should exist)
        Assert.DoesNotContain("public required string Type { get; set; }", result);
        Assert.Contains("public required string TypeValue { get; set; }", result);
    }

    [Fact]
    public void NonRecordWithTypeProperty_KeepsPropertyAsType()
    {
        // Arrange: a non-record type with a "type" property — no conflict
        var registry = new TypeRegistry();
        var def = new LexiconDefinition
        {
            Type = "object",
            Properties = new Dictionary<string, LexiconDefinition>
            {
                ["type"] = new LexiconDefinition { Type = "string" },
            },
            RequiredRaw = CreateJsonArray("type"),
        };

        var sb = new SourceBuilder();

        // Act
        ObjectGenerator.GenerateClass(
            sb, "PostEntity", def, "app.bsky.feed.post#entity", registry,
            isRecord: false);

        var result = sb.ToString();

        // Assert: the property keeps its original name since there's no $type discriminator
        Assert.Contains("JsonPropertyName(\"type\")", result);
        Assert.Contains("public required string Type { get; set; }", result);
        Assert.DoesNotContain("TypeValue", result);
    }

    [Fact]
    public void RecordWithoutTypeProperty_GeneratesNormalProperties()
    {
        // Arrange: a record without a conflicting "type" property
        var registry = new TypeRegistry();
        var def = new LexiconDefinition
        {
            Type = "object",
            Properties = new Dictionary<string, LexiconDefinition>
            {
                ["text"] = new LexiconDefinition { Type = "string" },
                ["createdAt"] = new LexiconDefinition { Type = "string", Format = "datetime" },
            },
            RequiredRaw = CreateJsonArray("text", "createdAt"),
        };

        var sb = new SourceBuilder();

        // Act
        ObjectGenerator.GenerateClass(
            sb, "Post", def, "app.bsky.feed.post", registry,
            isRecord: true, recordType: "app.bsky.feed.post",
            typeId: "app.bsky.feed.post");

        var result = sb.ToString();

        // Assert: $type discriminator exists
        Assert.Contains("public string Type => RecordType;", result);
        // Assert: normal properties are not renamed
        Assert.Contains("public required string Text { get; set; }", result);
        Assert.Contains("public required System.DateTimeOffset CreatedAt { get; set; }", result);
        Assert.DoesNotContain("TypeValue", result);
    }

    [Fact]
    public void PropertyMatchingClassName_GetsSuffixed()
    {
        // Arrange: a property that matches the class name
        var registry = new TypeRegistry();
        var def = new LexiconDefinition
        {
            Type = "object",
            Properties = new Dictionary<string, LexiconDefinition>
            {
                ["reaction"] = new LexiconDefinition { Type = "string" },
            },
            RequiredRaw = CreateJsonArray("reaction"),
        };

        var sb = new SourceBuilder();

        // Act
        ObjectGenerator.GenerateClass(
            sb, "Reaction", def, "test.ns.reaction", registry, isRecord: false);

        var result = sb.ToString();

        // Assert: property is renamed to avoid matching class name
        Assert.Contains("ReactionValue", result);
        Assert.Contains("JsonPropertyName(\"reaction\")", result);
    }

    #region Test Helpers

    private static System.Text.Json.JsonElement CreateJsonArray(params string[] values)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(values);
        return System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
    }

    private static int CountOccurrences(string source, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    #endregion
}
