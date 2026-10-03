using System.Xml.Linq;
using Xunit;

namespace CarpaNet.UnitTests.Generation;

/// <summary>
/// Verifies that the MSBuild properties exposed by CarpaNet.targets reach the generator, under both
/// the current <c>CarpaNet_*</c> names and the legacy <c>CarpaNet_SourceGen_*</c> names.
/// </summary>
public class GeneratorBuildPropertyTests
{
    private const string Lexicon = """
        {
          "lexicon": 1,
          "id": "com.example.note",
          "defs": {
            "main": {
              "type": "record",
              "key": "tid",
              "record": {
                "type": "object",
                "required": ["text"],
                "properties": {
                  "text": { "type": "string", "maxLength": 300 }
                }
              }
            }
          }
        }
        """;

    public static IEnumerable<object[]> Prefixes => new[]
    {
        new object[] { "CarpaNet_" },
        new object[] { "CarpaNet_SourceGen_" },
    };

    [Fact]
    public void Defaults_UseDefaultNamesAndEmitValidation()
    {
        var run = GeneratorTestHarness.Run(new[] { Lexicon });

        GeneratorTestHarness.AssertCompiles(run);
        Assert.Contains("namespace ComExample;", run.AllSource);
        Assert.Contains("public sealed class ATProtoJsonContext", run.AllSource);
        Assert.Contains("public partial class ATProtoCborContext", run.AllSource);
        Assert.Contains("CarpaNet.Validation.ATStringLength(300", run.AllSource);
    }

    [Theory]
    [MemberData(nameof(Prefixes))]
    public void RootNamespace_IsApplied(string prefix)
    {
        var run = GeneratorTestHarness.Run(new[] { Lexicon }, new Dictionary<string, string>
        {
            [prefix + "RootNamespace"] = "My.Lexicons",
        });

        GeneratorTestHarness.AssertCompiles(run);
        Assert.Contains("namespace My.Lexicons.ComExample;", run.AllSource);
        Assert.Contains("namespace My.Lexicons.Json;", run.AllSource);
        Assert.Contains("namespace My.Lexicons.Cbor;", run.AllSource);
        Assert.DoesNotContain("namespace ComExample;", run.AllSource);
    }

    [Theory]
    [MemberData(nameof(Prefixes))]
    public void ContextNames_AreApplied(string prefix)
    {
        var run = GeneratorTestHarness.Run(new[] { Lexicon }, new Dictionary<string, string>
        {
            [prefix + "JsonContextName"] = "MyJsonContext",
            [prefix + "CborContextName"] = "MyCborContext",
        });

        GeneratorTestHarness.AssertCompiles(run);
        Assert.True(run.Sources.ContainsKey("MyJsonContext.g.cs"));
        Assert.Contains("public sealed class MyJsonContext", run.AllSource);
        Assert.Contains("public partial class MyCborContext", run.AllSource);
        Assert.DoesNotContain("class ATProtoJsonContext", run.AllSource);
        Assert.DoesNotContain("class ATProtoCborContext", run.AllSource);
    }

    [Theory]
    [MemberData(nameof(Prefixes))]
    public void EmitValidationAttributes_False_OmitsValidationAttributes(string prefix)
    {
        var run = GeneratorTestHarness.Run(new[] { Lexicon }, new Dictionary<string, string>
        {
            [prefix + "EmitValidationAttributes"] = "false",
        });

        GeneratorTestHarness.AssertCompiles(run);
        Assert.DoesNotContain("CarpaNet.Validation.AT", run.AllSource);
    }

    [Fact]
    public void CurrentName_TakesPrecedenceOverLegacyName()
    {
        var run = GeneratorTestHarness.Run(new[] { Lexicon }, new Dictionary<string, string>
        {
            ["CarpaNet_JsonContextName"] = "NewJsonContext",
            ["CarpaNet_SourceGen_JsonContextName"] = "OldJsonContext",
        });

        Assert.Contains("public sealed class NewJsonContext", run.AllSource);
        Assert.DoesNotContain("OldJsonContext", run.AllSource);
    }

    [Fact]
    public void EmptyCurrentName_FallsBackToLegacyName()
    {
        // MSBuild writes "build_property.X = " for unset compiler-visible properties
        var run = GeneratorTestHarness.Run(new[] { Lexicon }, new Dictionary<string, string>
        {
            ["CarpaNet_RootNamespace"] = "",
            ["CarpaNet_SourceGen_RootNamespace"] = "Legacy.Root",
        });

        Assert.Contains("namespace Legacy.Root.ComExample;", run.AllSource);
    }

    [Theory]
    [InlineData("src/CarpaNet/build/CarpaNet.targets")]
    [InlineData("src/CarpaNet.SourceGen/build/CarpaNet.SourceGen.targets")]
    public void Targets_ExposeCurrentAndLegacyPropertyNames(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        var visible = XDocument.Load(path)
            .Descendants()
            .Where(e => e.Name.LocalName == "CompilerVisibleProperty")
            .Select(e => (string?)e.Attribute("Include"))
            .ToHashSet();

        foreach (var name in new[] { "RootNamespace", "EmitValidationAttributes", "CborContextName", "JsonContextName" })
        {
            Assert.Contains("CarpaNet_" + name, visible);
            Assert.Contains("CarpaNet_SourceGen_" + name, visible);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CarpaNet.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (CarpaNet.slnx).");
    }
}
