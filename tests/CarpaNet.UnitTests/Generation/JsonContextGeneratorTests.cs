using CarpaNet.Generation;

using Xunit;

namespace CarpaNet.UnitTests.Generation;

public class JsonContextGeneratorTests
{
    [Theory]
    [InlineData("string", "String")]
    [InlineData("long", "Int64")]
    [InlineData("bool", "Boolean")]
    [InlineData("int", "Int32")]
    [InlineData("byte[]", "ByteArray")]
    [InlineData("object", "Object")]
    [InlineData("System.DateTimeOffset", "System_DateTimeOffset")]
    [InlineData("System.Collections.Generic.List<string>", "System_Collections_Generic_List_string_")]
    public void ToBuiltInSuffix_ProducesValidIdentifier(string typeName, string expected)
    {
        var result = JsonContextGenerator.ToBuiltInSuffix(typeName);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToBuiltInSuffix_NestedGenericType_SanitizesAngleBrackets()
    {
        var result = JsonContextGenerator.ToBuiltInSuffix("System.Collections.Generic.List<string>");

        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.Equal("System_Collections_Generic_List_string_", result);
    }
}
