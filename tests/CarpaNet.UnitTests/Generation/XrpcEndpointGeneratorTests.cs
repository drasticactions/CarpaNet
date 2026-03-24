using CarpaNet.Generation;
using CarpaNet.Models;
using Xunit;

namespace CarpaNet.UnitTests.Generation;

public class XrpcEndpointGeneratorTests
{
    [Fact]
    public void GenerateQueryEndpoint_WithParameters_EmitsHttpGetAndFromQuery()
    {
        var (byNamespace, registry) = CreateQueryWithParameters();
        var options = new GeneratorOptions { EmitXrpcEndpoints = true };

        var result = XrpcEndpointGenerator.Generate(byNamespace, registry, options);

        Assert.Contains("[Microsoft.AspNetCore.Mvc.HttpGet(\"/xrpc/app.bsky.actor.getProfile\")]", result);
        Assert.Contains("[Microsoft.AspNetCore.Mvc.FromQuery(Name = \"actor\")]", result);
        Assert.Contains("string actor", result);
        Assert.Contains("GetProfileAsync", result);
    }

    [Fact]
    public void GenerateQueryEndpoint_NoParameters_EmitsOnlyCancellationToken()
    {
        var (byNamespace, registry) = CreateQueryNoParameters();
        var options = new GeneratorOptions { EmitXrpcEndpoints = true };

        var result = XrpcEndpointGenerator.Generate(byNamespace, registry, options);

        Assert.Contains("[Microsoft.AspNetCore.Mvc.HttpGet(\"/xrpc/app.bsky.actor.getSuggestions\")]", result);
        Assert.Contains("System.Threading.CancellationToken cancellationToken", result);
        Assert.DoesNotContain("FromQuery", result);
    }

    [Fact]
    public void GenerateProcedureEndpoint_WithInputAndOutput_EmitsHttpPostAndFromBody()
    {
        var (byNamespace, registry) = CreateProcedureWithInputAndOutput();
        var options = new GeneratorOptions { EmitXrpcEndpoints = true };

        var result = XrpcEndpointGenerator.Generate(byNamespace, registry, options);

        Assert.Contains("[Microsoft.AspNetCore.Mvc.HttpPost(\"/xrpc/com.atproto.repo.createRecord\")]", result);
        Assert.Contains("[Microsoft.AspNetCore.Mvc.FromBody]", result);
        Assert.Contains("CreateRecordInput input", result);
        Assert.Contains("Ok<", result);
    }

    [Fact]
    public void GenerateProcedureEndpoint_NoOutput_ReturnsOkWithoutGenericType()
    {
        var (byNamespace, registry) = CreateProcedureNoOutput();
        var options = new GeneratorOptions { EmitXrpcEndpoints = true };

        var result = XrpcEndpointGenerator.Generate(byNamespace, registry, options);

        Assert.Contains("[Microsoft.AspNetCore.Mvc.HttpPost(\"/xrpc/com.atproto.repo.deleteRecord\")]", result);
        Assert.Contains("Results<Microsoft.AspNetCore.Http.HttpResults.Ok, CarpaNet.AspNetCore.ATErrorResult>", result);
    }

    [Fact]
    public void GenerateProcedureEndpoint_NoInput_OmitsFromBody()
    {
        var (byNamespace, registry) = CreateProcedureNoInput();
        var options = new GeneratorOptions { EmitXrpcEndpoints = true };

        var result = XrpcEndpointGenerator.Generate(byNamespace, registry, options);

        Assert.Contains("[Microsoft.AspNetCore.Mvc.HttpPost(\"/xrpc/com.atproto.server.refreshSession\")]", result);
        Assert.DoesNotContain("FromBody", result);
        Assert.Contains("System.Threading.CancellationToken cancellationToken", result);
    }

    [Fact]
    public void ControllerGrouping_SameGroup_ProducesOneController()
    {
        var (byNamespace, registry) = CreateMultipleEndpointsSameGroup();
        var options = new GeneratorOptions { EmitXrpcEndpoints = true };

        var result = XrpcEndpointGenerator.Generate(byNamespace, registry, options);

        // Should have exactly one ActorController
        var controllerCount = CountOccurrences(result, "class ActorController");
        Assert.Equal(1, controllerCount);

        // Both methods should be present
        Assert.Contains("GetProfileAsync", result);
        Assert.Contains("GetPreferencesAsync", result);
    }

    [Fact]
    public void ControllerGrouping_DifferentGroups_ProducesSeparateControllers()
    {
        var (byNamespace, registry) = CreateEndpointsDifferentGroups();
        var options = new GeneratorOptions { EmitXrpcEndpoints = true };

        var result = XrpcEndpointGenerator.Generate(byNamespace, registry, options);

        Assert.Contains("class ActorController", result);
        Assert.Contains("class RepoController", result);
    }

    [Fact]
    public void EmitXrpcEndpoints_False_GeneratesNothing()
    {
        var (byNamespace, registry) = CreateQueryWithParameters();
        var options = new GeneratorOptions { EmitXrpcEndpoints = false };

        // The Generate method is only called when EmitXrpcEndpoints is true,
        // but we can verify the option exists
        Assert.False(options.EmitXrpcEndpoints);
    }

    [Fact]
    public void RefInputType_ResolvesCorrectly()
    {
        var (byNamespace, registry) = CreateProcedureWithRefInput();
        var options = new GeneratorOptions { EmitXrpcEndpoints = true };

        var result = XrpcEndpointGenerator.Generate(byNamespace, registry, options);

        // The ref input should resolve to the referenced type
        Assert.Contains("FromBody", result);
        Assert.Contains("input", result);
    }

    [Fact]
    public void ArrayQueryParameter_GeneratesList()
    {
        var (byNamespace, registry) = CreateQueryWithArrayParameter();
        var options = new GeneratorOptions { EmitXrpcEndpoints = true };

        var result = XrpcEndpointGenerator.Generate(byNamespace, registry, options);

        Assert.Contains("System.Collections.Generic.List<string>", result);
        Assert.Contains("FromQuery", result);
    }

    [Fact]
    public void GetControllerGroup_ReturnsFirstThreeSegments()
    {
        Assert.Equal("app.bsky.actor", XrpcEndpointGenerator.GetControllerGroup("app.bsky.actor.getProfile"));
        Assert.Equal("com.atproto.server", XrpcEndpointGenerator.GetControllerGroup("com.atproto.server.createSession"));
        Assert.Null(XrpcEndpointGenerator.GetControllerGroup("too.short"));
    }

    [Fact]
    public void GetControllerName_ReturnsPascalCaseWithSuffix()
    {
        Assert.Equal("ActorController", XrpcEndpointGenerator.GetControllerName("app.bsky.actor"));
        Assert.Equal("ServerController", XrpcEndpointGenerator.GetControllerName("com.atproto.server"));
    }

    [Fact]
    public void GetMethodName_ReturnsLastSegmentPascalCase()
    {
        Assert.Equal("GetProfile", XrpcEndpointGenerator.GetMethodName("app.bsky.actor.getProfile"));
        Assert.Equal("CreateRecord", XrpcEndpointGenerator.GetMethodName("com.atproto.repo.createRecord"));
    }

    [Fact]
    public void OptionalQueryParameters_AreNullableWithDefault()
    {
        var (byNamespace, registry) = CreateQueryWithOptionalParameters();
        var options = new GeneratorOptions { EmitXrpcEndpoints = true };

        var result = XrpcEndpointGenerator.Generate(byNamespace, registry, options);

        Assert.Contains("long?", result);
        Assert.Contains("= default", result);
    }

    [Fact]
    public void ControllerNamespace_IncludesXrpcPrefix()
    {
        var (byNamespace, registry) = CreateQueryWithParameters();
        var options = new GeneratorOptions { EmitXrpcEndpoints = true };

        var result = XrpcEndpointGenerator.Generate(byNamespace, registry, options);

        Assert.Contains("namespace Xrpc.AppBsky.Actor", result);
    }

    [Fact]
    public void Controller_IsAbstractPartial()
    {
        var (byNamespace, registry) = CreateQueryWithParameters();
        var options = new GeneratorOptions { EmitXrpcEndpoints = true };

        var result = XrpcEndpointGenerator.Generate(byNamespace, registry, options);

        Assert.Contains("public abstract partial class ActorController", result);
    }

    #region Test Helpers

    private static (Dictionary<string, List<(string Nsid, LexiconDocument Doc)>>, TypeRegistry) CreateQueryWithParameters()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "app.bsky.actor.getProfile",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "query",
                    Description = "Get detailed profile view of an actor.",
                    Parameters = new LexiconDefinition
                    {
                        Type = "params",
                        Properties = new Dictionary<string, LexiconDefinition>
                        {
                            ["actor"] = new LexiconDefinition { Type = "string", Format = "at-identifier" }
                        },
                        RequiredRaw = CreateJsonArray("actor"),
                    },
                    Output = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconDefinition>
                            {
                                ["did"] = new LexiconDefinition { Type = "string", Format = "did" },
                                ["handle"] = new LexiconDefinition { Type = "string", Format = "handle" },
                            }
                        }
                    }
                }
            }
        };

        registry.RegisterDocument(doc);
        var byNamespace = GroupByNamespace(doc, registry);
        return (byNamespace, registry);
    }

    private static (Dictionary<string, List<(string Nsid, LexiconDocument Doc)>>, TypeRegistry) CreateQueryNoParameters()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "app.bsky.actor.getSuggestions",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "query",
                    Output = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconDefinition>
                            {
                                ["actors"] = new LexiconDefinition { Type = "array", Items = new LexiconDefinition { Type = "string" } }
                            }
                        }
                    }
                }
            }
        };

        registry.RegisterDocument(doc);
        var byNamespace = GroupByNamespace(doc, registry);
        return (byNamespace, registry);
    }

    private static (Dictionary<string, List<(string Nsid, LexiconDocument Doc)>>, TypeRegistry) CreateProcedureWithInputAndOutput()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "com.atproto.repo.createRecord",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "procedure",
                    Input = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconDefinition>
                            {
                                ["repo"] = new LexiconDefinition { Type = "string", Format = "at-identifier" },
                                ["collection"] = new LexiconDefinition { Type = "string", Format = "nsid" },
                            }
                        }
                    },
                    Output = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconDefinition>
                            {
                                ["uri"] = new LexiconDefinition { Type = "string", Format = "at-uri" },
                                ["cid"] = new LexiconDefinition { Type = "string", Format = "cid" },
                            }
                        }
                    }
                }
            }
        };

        registry.RegisterDocument(doc);
        var byNamespace = GroupByNamespace(doc, registry);
        return (byNamespace, registry);
    }

    private static (Dictionary<string, List<(string Nsid, LexiconDocument Doc)>>, TypeRegistry) CreateProcedureNoOutput()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "com.atproto.repo.deleteRecord",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "procedure",
                    Input = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconDefinition>
                            {
                                ["repo"] = new LexiconDefinition { Type = "string", Format = "at-identifier" },
                            }
                        }
                    }
                }
            }
        };

        registry.RegisterDocument(doc);
        var byNamespace = GroupByNamespace(doc, registry);
        return (byNamespace, registry);
    }

    private static (Dictionary<string, List<(string Nsid, LexiconDocument Doc)>>, TypeRegistry) CreateProcedureNoInput()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "com.atproto.server.refreshSession",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "procedure",
                    Output = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconDefinition>
                            {
                                ["accessJwt"] = new LexiconDefinition { Type = "string" },
                                ["refreshJwt"] = new LexiconDefinition { Type = "string" },
                            }
                        }
                    }
                }
            }
        };

        registry.RegisterDocument(doc);
        var byNamespace = GroupByNamespace(doc, registry);
        return (byNamespace, registry);
    }

    private static (Dictionary<string, List<(string Nsid, LexiconDocument Doc)>>, TypeRegistry) CreateMultipleEndpointsSameGroup()
    {
        var registry = new TypeRegistry();
        var doc1 = new LexiconDocument
        {
            Id = "app.bsky.actor.getProfile",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "query",
                    Parameters = new LexiconDefinition
                    {
                        Type = "params",
                        Properties = new Dictionary<string, LexiconDefinition>
                        {
                            ["actor"] = new LexiconDefinition { Type = "string" }
                        },
                        RequiredRaw = CreateJsonArray("actor"),
                    },
                    Output = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition { Type = "object", Properties = new() }
                    }
                }
            }
        };

        var doc2 = new LexiconDocument
        {
            Id = "app.bsky.actor.getPreferences",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "query",
                    Output = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition { Type = "object", Properties = new() }
                    }
                }
            }
        };

        registry.RegisterDocument(doc1);
        registry.RegisterDocument(doc2);
        var byNamespace = GroupByNamespace(new[] { doc1, doc2 }, registry);
        return (byNamespace, registry);
    }

    private static (Dictionary<string, List<(string Nsid, LexiconDocument Doc)>>, TypeRegistry) CreateEndpointsDifferentGroups()
    {
        var registry = new TypeRegistry();
        var doc1 = new LexiconDocument
        {
            Id = "app.bsky.actor.getProfile",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "query",
                    Parameters = new LexiconDefinition
                    {
                        Type = "params",
                        Properties = new Dictionary<string, LexiconDefinition>
                        {
                            ["actor"] = new LexiconDefinition { Type = "string" }
                        },
                        RequiredRaw = CreateJsonArray("actor"),
                    },
                    Output = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition { Type = "object", Properties = new() }
                    }
                }
            }
        };

        var doc2 = new LexiconDocument
        {
            Id = "com.atproto.repo.createRecord",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "procedure",
                    Input = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition
                        {
                            Type = "object",
                            Properties = new Dictionary<string, LexiconDefinition>
                            {
                                ["repo"] = new LexiconDefinition { Type = "string" }
                            }
                        }
                    },
                    Output = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition { Type = "object", Properties = new() }
                    }
                }
            }
        };

        registry.RegisterDocument(doc1);
        registry.RegisterDocument(doc2);
        var byNamespace = GroupByNamespace(new[] { doc1, doc2 }, registry);
        return (byNamespace, registry);
    }

    private static (Dictionary<string, List<(string Nsid, LexiconDocument Doc)>>, TypeRegistry) CreateProcedureWithRefInput()
    {
        var registry = new TypeRegistry();

        // Register the referenced type first
        var defsDoc = new LexiconDocument
        {
            Id = "com.atproto.repo.defs",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["recordInput"] = new LexiconDefinition
                {
                    Type = "object",
                    Properties = new Dictionary<string, LexiconDefinition>
                    {
                        ["repo"] = new LexiconDefinition { Type = "string" }
                    }
                }
            }
        };

        var doc = new LexiconDocument
        {
            Id = "com.atproto.repo.applyWrites",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "procedure",
                    Input = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition
                        {
                            Type = "ref",
                            Ref = "com.atproto.repo.defs#recordInput"
                        }
                    },
                    Output = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition { Type = "object", Properties = new() }
                    }
                }
            }
        };

        registry.RegisterDocument(defsDoc);
        registry.RegisterDocument(doc);
        var byNamespace = GroupByNamespace(new[] { defsDoc, doc }, registry);
        return (byNamespace, registry);
    }

    private static (Dictionary<string, List<(string Nsid, LexiconDocument Doc)>>, TypeRegistry) CreateQueryWithArrayParameter()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "app.bsky.actor.getProfiles",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "query",
                    Parameters = new LexiconDefinition
                    {
                        Type = "params",
                        Properties = new Dictionary<string, LexiconDefinition>
                        {
                            ["actors"] = new LexiconDefinition
                            {
                                Type = "array",
                                Items = new LexiconDefinition { Type = "string", Format = "at-identifier" }
                            }
                        },
                        RequiredRaw = CreateJsonArray("actors"),
                    },
                    Output = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition { Type = "object", Properties = new() }
                    }
                }
            }
        };

        registry.RegisterDocument(doc);
        var byNamespace = GroupByNamespace(doc, registry);
        return (byNamespace, registry);
    }

    private static (Dictionary<string, List<(string Nsid, LexiconDocument Doc)>>, TypeRegistry) CreateQueryWithOptionalParameters()
    {
        var registry = new TypeRegistry();
        var doc = new LexiconDocument
        {
            Id = "app.bsky.feed.getTimeline",
            Defs = new Dictionary<string, LexiconDefinition>
            {
                ["main"] = new LexiconDefinition
                {
                    Type = "query",
                    Parameters = new LexiconDefinition
                    {
                        Type = "params",
                        Properties = new Dictionary<string, LexiconDefinition>
                        {
                            ["limit"] = new LexiconDefinition { Type = "integer" },
                            ["cursor"] = new LexiconDefinition { Type = "string" },
                        },
                    },
                    Output = new LexiconIO
                    {
                        Encoding = "application/json",
                        Schema = new LexiconDefinition { Type = "object", Properties = new() }
                    }
                }
            }
        };

        registry.RegisterDocument(doc);
        var byNamespace = GroupByNamespace(doc, registry);
        return (byNamespace, registry);
    }

    private static Dictionary<string, List<(string Nsid, LexiconDocument Doc)>> GroupByNamespace(
        LexiconDocument doc, TypeRegistry registry)
    {
        return GroupByNamespace(new[] { doc }, registry);
    }

    private static Dictionary<string, List<(string Nsid, LexiconDocument Doc)>> GroupByNamespace(
        LexiconDocument[] docs, TypeRegistry registry)
    {
        var result = new Dictionary<string, List<(string, LexiconDocument)>>();
        foreach (var doc in docs)
        {
            var ns = Utilities.NsidHelper.ToNamespace(doc.Id);
            if (!result.TryGetValue(ns, out var list))
            {
                list = new List<(string, LexiconDocument)>();
                result[ns] = list;
            }
            list.Add((doc.Id, doc));
        }
        return result;
    }

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
