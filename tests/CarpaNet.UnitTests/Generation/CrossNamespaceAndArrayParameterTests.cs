using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace CarpaNet.UnitTests.Generation;

/// <summary>
/// Regression tests for two generator bugs found by compiling every atproto lexicon:
/// a ref to an array-of-union def in another namespace emitted an unqualified interface name
/// (tools.ozone.moderation.getAccountPreferences), and integer-array query parameters emitted
/// <c>item?.ToString()</c> on a <c>long</c> (tools.ozone.*.getAssignments).
/// </summary>
public class CrossNamespaceAndArrayParameterTests
{
    private const string Defs = """
        {
          "lexicon": 1,
          "id": "com.example.alpha.defs",
          "defs": {
            "preferences": {
              "type": "array",
              "items": { "type": "union", "refs": ["#first", "#second"] }
            },
            "first": { "type": "object", "properties": { "a": { "type": "string" } } },
            "second": { "type": "object", "properties": { "b": { "type": "integer" } } }
          }
        }
        """;

    private const string GetPreferences = """
        {
          "lexicon": 1,
          "id": "org.example.beta.getPreferences",
          "defs": {
            "main": {
              "type": "query",
              "output": {
                "encoding": "application/json",
                "schema": {
                  "type": "object",
                  "required": ["preferences"],
                  "properties": { "preferences": { "type": "ref", "ref": "com.example.alpha.defs#preferences" } }
                }
              }
            }
          }
        }
        """;

    private const string GetAssignments = """
        {
          "lexicon": 1,
          "id": "org.example.beta.getAssignments",
          "defs": {
            "main": {
              "type": "query",
              "parameters": {
                "type": "params",
                "properties": {
                  "ids": { "type": "array", "items": { "type": "integer" } },
                  "flags": { "type": "array", "items": { "type": "boolean" } }
                }
              },
              "output": {
                "encoding": "application/json",
                "schema": { "type": "object", "properties": { "count": { "type": "integer" } } }
              }
            }
          }
        }
        """;

    [Fact]
    public void CrossNamespaceArrayOfUnionRef_Compiles()
    {
        var run = GeneratorTestHarness.Run(new[] { Defs, GetPreferences });

        GeneratorTestHarness.AssertCompiles(run);
        Assert.Contains("ComExample.Alpha.IDefsPreferences", run.Sources.Values.First(s => s.Contains("GetPreferencesOutput")));
    }

    [Fact]
    public void IntegerAndBooleanArrayParameters_FormatInvariantly()
    {
        var run = GeneratorTestHarness.Run(new[] { GetAssignments });
        var assembly = GeneratorTestHarness.CompileAndLoad(run);

        var parametersType = assembly.GetType("OrgExample.Beta.GetAssignmentsParameters")!;
        var parameters = System.Activator.CreateInstance(parametersType)!;
        parametersType.GetProperty("Ids")!.SetValue(parameters, new List<long> { 12, -3 });
        parametersType.GetProperty("Flags")!.SetValue(parameters, new List<bool> { true, false });

        var query = ((IEnumerable<KeyValuePair<string, string>>)parametersType.GetMethod("ToQueryParameters")!.Invoke(parameters, null)!).ToList();

        Assert.Equal(
            new[] { ("ids", "12"), ("ids", "-3"), ("flags", "true"), ("flags", "false") },
            query.Select(kv => (kv.Key, kv.Value)).ToArray());
    }
}
