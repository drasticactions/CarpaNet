using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CarpaNet.UnitTests.Generation;

/// <summary>
/// Runs <see cref="LexiconGenerator"/> end to end over in-memory lexicon JSON (no MSBuild, no network),
/// optionally compiling and loading the generated code so it can be exercised at runtime.
/// </summary>
internal static class GeneratorTestHarness
{
    /// <summary>
    /// The result of a generator run.
    /// </summary>
    internal sealed class GeneratorRun
    {
        public required CSharpCompilation Compilation { get; init; }

        public required ImmutableArray<Diagnostic> GeneratorDiagnostics { get; init; }

        /// <summary>Generated sources keyed by hint name (e.g. "ATProtoExtensions.g.cs").</summary>
        public required IReadOnlyDictionary<string, string> Sources { get; init; }

        /// <summary>All generated sources concatenated.</summary>
        public string AllSource => string.Join("\n", Sources.Values);

        /// <summary>Errors from compiling the generated code together with any extra sources.</summary>
        public IReadOnlyList<Diagnostic> CompilationErrors =>
            Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    }

    /// <summary>
    /// Runs the generator over the given lexicon documents.
    /// </summary>
    /// <param name="lexiconJson">Lexicon documents as JSON text.</param>
    /// <param name="buildProperties">MSBuild properties exposed to the generator, without the "build_property." prefix.</param>
    public static GeneratorRun Run(IEnumerable<string> lexiconJson, IDictionary<string, string>? buildProperties = null)
    {
        var additionalTexts = lexiconJson
            .Select((json, i) => (AdditionalText)new InMemoryAdditionalText($"/lexicons/lexicon{i}.json", json))
            .ToImmutableArray();

        var globalOptions = new Dictionary<string, string>(StringComparer.Ordinal);
        if (buildProperties != null)
        {
            foreach (var kvp in buildProperties)
            {
                globalOptions["build_property." + kvp.Key] = kvp.Value;
            }
        }

        var optionsProvider = new TestAnalyzerConfigOptionsProvider(globalOptions);

        var compilation = CSharpCompilation.Create(
            "GeneratedLexicons_" + Guid.NewGuid().ToString("N"),
            Array.Empty<SyntaxTree>(),
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(
            new[] { new LexiconGenerator().AsSourceGenerator() },
            additionalTexts,
            new CSharpParseOptions(LanguageVersion.Latest),
            optionsProvider);

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        var sources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

        return new GeneratorRun
        {
            Compilation = (CSharpCompilation)outputCompilation,
            GeneratorDiagnostics = diagnostics,
            Sources = sources,
        };
    }

    /// <summary>
    /// Asserts that the generated code compiles without errors and returns the formatted errors otherwise.
    /// </summary>
    public static void AssertCompiles(GeneratorRun run)
    {
        var errors = run.CompilationErrors;
        if (errors.Count > 0)
        {
            var message = new StringBuilder("Generated code failed to compile:\n");
            foreach (var error in errors.Take(25))
            {
                message.AppendLine(error.ToString());
            }

            throw new Xunit.Sdk.XunitException(message.ToString());
        }
    }

    /// <summary>
    /// Compiles the generated code and loads it into a collectible load context.
    /// </summary>
    public static Assembly CompileAndLoad(GeneratorRun run)
    {
        AssertCompiles(run);

        using var stream = new MemoryStream();
        var emitResult = run.Compilation.Emit(stream);
        if (!emitResult.Success)
        {
            throw new Xunit.Sdk.XunitException("Emit failed:\n" + string.Join("\n", emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        stream.Position = 0;
        var context = new AssemblyLoadContext("GeneratedLexicons", isCollectible: true);
        return context.LoadFromStream(stream);
    }

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Avoid generator/test types (some share the CarpaNet namespace) leaking into the consumer compilation
            "CarpaNet.SourceGen",
            "CarpaNet.UnitTests",
        };

        var paths = tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !excluded.Contains(Path.GetFileNameWithoutExtension(p)))
            .ToList();

        // Make sure the runtime library and CBOR dependency are present even if not yet in the TPA list
        foreach (var assembly in new[] { typeof(CarpaNet.IATProtoClient).Assembly, typeof(System.Formats.Cbor.CborReader).Assembly })
        {
            if (!paths.Contains(assembly.Location, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(assembly.Location);
            }
        }

        return paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToImmutableArray();
    });

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text, Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private static readonly TestAnalyzerConfigOptions LexiconFileOptions = new(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.IsATProtoLexicon"] = "true",
        });

        public TestAnalyzerConfigOptionsProvider(Dictionary<string, string> globalOptions)
        {
            GlobalOptions = new TestAnalyzerConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => TestAnalyzerConfigOptions.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => LexiconFileOptions;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        public static readonly TestAnalyzerConfigOptions Empty = new(new Dictionary<string, string>());

        private readonly Dictionary<string, string> _values;

        public TestAnalyzerConfigOptions(Dictionary<string, string> values)
        {
            _values = values;
        }

        public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
            => _values.TryGetValue(key, out value);
    }
}
