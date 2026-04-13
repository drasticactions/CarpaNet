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

        Assert.Contains("new CarpaNet.Cbor.CborListTypeInfo<global::System.Collections.Generic.List<string>>(new CarpaNet.Cbor.CborListTypeInfo<string>(new CarpaNet.Cbor.Converters.StringCborConverter()))", result);
    }

    [Fact]
    public void RefToArrayOfArrayRefs_UsesListConverterNotStringConverter()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "test.nested.refarray",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["transformRow"] = new LexiconDefinition
                {
                    Type = "array",
                    Items = new LexiconDefinition { Type = "string" },
                },
                ["transform"] = new LexiconDefinition
                {
                    Type = "array",
                    Items = new LexiconDefinition
                    {
                        Type = "ref",
                        Ref = "#transformRow",
                    },
                },
                ["main"] = new LexiconDefinition
                {
                    Type = "object",
                    Properties = new Dictionary<string, LexiconDefinition>
                    {
                        ["transform"] = new LexiconDefinition
                        {
                            Type = "ref",
                            Ref = "#transform",
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
            sb, "TestNested.Refarray.DefsMain", "TestNested_Refarray_DefsMain",
            mainDef, "test.nested.refarray", registry, options);

        var result = sb.ToString();

        Assert.Contains("new CarpaNet.Cbor.CborListTypeInfo<global::System.Collections.Generic.List<string>>(new CarpaNet.Cbor.CborListTypeInfo<string>(new CarpaNet.Cbor.Converters.StringCborConverter()))", result);
    }
}
