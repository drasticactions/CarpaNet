using CarpaNet.Generation;
using CarpaNet.Models;
using CarpaNet.Utilities;

using Xunit;

namespace CarpaNet.UnitTests.Generation;

public class CborContextGeneratorTests
{
    [Fact]
    public void ArrayOfArrayRef_UsesListConverterNotStringConverter()
    {
        // Arrange: reproduce the MebiByte pattern where "transform" is an array
        // of "transformRow" refs, and "transformRow" is itself an array of strings.
        // Without the fix, the inner List<string> element gets a StringCborConverter
        // instead of a CborListTypeInfo<string>.
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "test.nested.array",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["transformRow"] = new LexiconDefinition
                {
                    Type = "array",
                    Items = new LexiconDefinition { Type = "string" },
                },
                ["main"] = new LexiconDefinition
                {
                    Type = "object",
                    Properties = new Dictionary<string, LexiconDefinition>
                    {
                        ["transform"] = new LexiconDefinition
                        {
                            Type = "array",
                            Items = new LexiconDefinition
                            {
                                Type = "ref",
                                Ref = "#transformRow",
                            },
                        },
                    },
                },
            },
        };
        registry.RegisterDocument(doc);

        var sb = new SourceBuilder();
        var options = new GeneratorOptions();
        var mainDef = doc.Defs["main"];

        // Act
        CborContextGenerator.GenerateCborTypeInfo(
            sb, "TestNested.Array.DefsMain", "TestNested_Array_DefsMain",
            mainDef, "test.nested.array", registry, options);

        var result = sb.ToString();

        // Assert: the transform property should wrap the inner List<string> in a
        // CborListTypeInfo, not pass a StringCborConverter directly as the element
        // converter for List<List<string>>.
        // Correct: CborListTypeInfo<List<string>>( CborListTypeInfo<string>( StringCborConverter ) )
        // Wrong:   CborListTypeInfo<List<string>>( StringCborConverter )
        Assert.Contains("new CarpaNet.Cbor.CborListTypeInfo<global::System.Collections.Generic.List<string>>(new CarpaNet.Cbor.CborListTypeInfo<string>(new CarpaNet.Cbor.Converters.StringCborConverter()))", result);
    }
}
