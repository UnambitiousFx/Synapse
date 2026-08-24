using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using UnambitiousFx.Synapse.Endpoints;
using UnambitiousFx.Synapse.Endpoints.Generator;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

internal static class GeneratorHarness
{
    internal static string GetFile(string source, string fileName)
    {
        return TryGetFile(source, fileName)
               ?? throw new InvalidOperationException($"The generator did not emit '{fileName}'.");
    }

    internal static string? TryGetFile(string source, string fileName)
    {
        var (_, trees) = Run(source, optionsProvider: null);
        var tree = trees.FirstOrDefault(t => t.FilePath.EndsWith(fileName, StringComparison.Ordinal));
        return tree?.ToString();
    }

    /// <summary>
    ///     Same as <see cref="GetFile" />, but with <c>build_property.RootNamespace</c> set to
    ///     <paramref name="rootNamespace" /> — including to the empty string, which is what a project
    ///     declaring <c>&lt;RootNamespace&gt;&lt;/RootNamespace&gt;</c> actually surfaces to a
    ///     generator, and which a plain null check never catches. Pass <see langword="null" /> to
    ///     leave the property unset instead.
    /// </summary>
    internal static string GetFileWithRootNamespace(string source, string fileName, string? rootNamespace)
    {
        var provider = rootNamespace is null
            ? null
            : new TestAnalyzerConfigOptionsProvider(
                new Dictionary<string, string> { ["build_property.RootNamespace"] = rootNamespace });

        var (_, trees) = Run(source, provider);
        var tree = trees.FirstOrDefault(t => t.FilePath.EndsWith(fileName, StringComparison.Ordinal));
        return tree?.ToString()
               ?? throw new InvalidOperationException($"The generator did not emit '{fileName}'.");
    }

    internal static ImmutableArray<Diagnostic> GetDiagnostics(string source)
    {
        var (diagnostics, _) = Run(source, optionsProvider: null);
        return diagnostics;
    }

    /// <summary>
    ///     Same as <see cref="GetDiagnostics(string)" />, but with additional metadata references —
    ///     used to exercise SYNE008's reference-graph scan, where the <c>JsonSerializerContext</c>
    ///     under test lives in a separately compiled assembly rather than <paramref name="source" />
    ///     itself. See <see cref="CompileToReference" />.
    /// </summary>
    internal static ImmutableArray<Diagnostic> GetDiagnostics(string source, params MetadataReference[] extraReferences)
    {
        var driver = CSharpGeneratorDriver.Create(new EndpointsGenerator());
        var compilation = CreateCompilation(source).AddReferences(extraReferences);
        var result = driver.RunGenerators(compilation).GetRunResult();
        return result.Diagnostics;
    }

    /// <summary>
    ///     Compiles <paramref name="source" /> into an in-memory assembly and returns a
    ///     <see cref="MetadataReference" /> to it, so a test can put a type (for example a
    ///     <c>JsonSerializerContext</c>) in a referenced assembly instead of the compilation under
    ///     test — the case <c>EndpointsGenerator</c>'s reference-graph scan for SYNE008 exists to
    ///     handle. <paramref name="extraReferences" /> lets one compiled-to-reference assembly in
    ///     turn reference another (for example a "Microsoft."-named assembly whose
    ///     <c>JsonSerializerContext</c> registers a type actually declared in a third, differently
    ///     named assembly) — needed to keep "the type under test" and "whatever independently
    ///     exempts a type from being checked at all" from accidentally living in the same assembly,
    ///     which would make a fixture pass without ever exercising what it claims to.
    /// </summary>
    internal static MetadataReference CompileToReference(string source, string assemblyName,
        params MetadataReference[] extraReferences)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            GetMetadataReferences().Concat(extraReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success,
            "Failed to compile the reference assembly '" + assemblyName + "': " +
            string.Join("; ", emitResult.Diagnostics.Select(static d => d.ToString())));

        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream);
    }

    internal static void AssertGeneratedCompiles(string source)
    {
        AssertGeneratedCompiles(source, optionsProvider: null);
    }

    /// <summary>
    ///     Same as <see cref="AssertGeneratedCompiles(string)" />, but with
    ///     <c>build_property.RootNamespace</c> set to <paramref name="rootNamespace" /> rather than
    ///     left unset. Every other test in this suite leaves it unset, which makes
    ///     <c>EndpointsGenerator</c> fall back to the compilation's assembly name
    ///     (<c>"TestAssembly"</c>). Passing an explicit <paramref name="rootNamespace" /> pins that the
    ///     property is honoured when present, and one disjoint from
    ///     <c>UnambitiousFx.Synapse.Endpoints</c> (for example <c>"Acme.Api"</c>) additionally closes
    ///     the hole where an emitter that only resolves by namespace-nesting (an unqualified
    ///     extension-method call with no <c>using</c>, say) would pass by coincidence rather than by
    ///     being correct for a real consumer.
    /// </summary>
    internal static void AssertGeneratedCompilesWithRootNamespace(string source, string rootNamespace)
    {
        AssertGeneratedCompiles(source,
            new TestAnalyzerConfigOptionsProvider(
                new Dictionary<string, string> { ["build_property.RootNamespace"] = rootNamespace }));
    }

    private static void AssertGeneratedCompiles(string source, AnalyzerConfigOptionsProvider? optionsProvider)
    {
        var compilation = CreateCompilation(source);
        var driver = optionsProvider is null
            ? CSharpGeneratorDriver.Create(new EndpointsGenerator())
            : CSharpGeneratorDriver.Create([new EndpointsGenerator().AsSourceGenerator()], optionsProvider: optionsProvider);
        var updatedDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);

        // A generator that throws mid-Analyze surfaces as CS8785, which the compiler treats as a
        // Warning by default — so checking only Error-severity diagnostics below would let a
        // crashing generator through silently. Treat CS8785 as fatal regardless of the severity the
        // driver assigned it.
        var generatorFailures = generatorDiagnostics
            .Where(d => d.Id == "CS8785")
            .ToArray();

        Assert.True(generatorFailures.Length == 0,
            "The generator itself threw: " + string.Join("; ", generatorFailures.Select(e => e.ToString())));

        var errors = updated.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(errors.Length == 0,
            "Generated code should compile, but got: " + string.Join("; ", errors.Select(e => e.ToString())));

        // A generator that silently emits nothing (e.g. because base-type matching failed for a
        // shape) introduces no diagnostics and would otherwise pass the checks above unnoticed.
        var driverResult = updatedDriver.GetRunResult();
        Assert.True(driverResult.GeneratedTrees.Length > 0,
            "The generator produced no source at all.");
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> Trees) Run(string source,
        AnalyzerConfigOptionsProvider? optionsProvider)
    {
        var driver = optionsProvider is null
            ? CSharpGeneratorDriver.Create(new EndpointsGenerator())
            : CSharpGeneratorDriver.Create([new EndpointsGenerator().AsSourceGenerator()],
                optionsProvider: optionsProvider);
        var result = driver.RunGenerators(CreateCompilation(source)).GetRunResult();
        return (result.Diagnostics, result.GeneratedTrees);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        return CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    /// <summary>
    ///     Copied from <c>Synapse.Generator.Tests.GeneratorBehaviorTests.GetMetadataReferences()</c> and
    ///     extended with the Synapse.Endpoints assembly and the ASP.NET Core reference assemblies:
    ///     discovered endpoint types derive from the four endpoint base classes (Synapse.Endpoints), and the
    ///     emitted <c>SynapseEndpointGroup.g.cs</c> names <c>Microsoft.AspNetCore.Routing.IEndpointRouteBuilder</c>.
    ///     The test project's <c>FrameworkReference</c> to <c>Microsoft.AspNetCore.App</c> puts the ASP.NET
    ///     Core shared-framework assemblies in TRUSTED_PLATFORM_ASSEMBLIES alongside the runtime ones, so no
    ///     separate reference-assembly lookup is needed here.
    /// </summary>
    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        // Load all trusted platform assemblies (covers System.Runtime, System.Collections, the ASP.NET Core
        // shared framework, etc. — the latter is present because this test project declares a
        // FrameworkReference to Microsoft.AspNetCore.App).
        var trustedPaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var refs = trustedPaths
            .Select(p => MetadataReference.CreateFromFile(p))
            .Cast<MetadataReference>()
            .ToList();

        // Add Synapse.Abstractions (IRequest, IStreamRequest, …)
        refs.Add(MetadataReference.CreateFromFile(typeof(UnambitiousFx.Synapse.Abstractions.IRequest).Assembly.Location));

        // Add UnambitiousFx.Functional (Result<T> used in interface signatures)
        refs.Add(MetadataReference.CreateFromFile(typeof(UnambitiousFx.Functional.Result).Assembly.Location));

        // Add Synapse.Endpoints (Endpoint<>, MappedEndpoint<>, StreamEndpoint<>, IEndpointGroup, EndpointMetadata, …)
        refs.Add(MetadataReference.CreateFromFile(typeof(EndpointBase).Assembly.Location));

        return refs;
    }

    /// <summary>
    ///     Minimal <see cref="AnalyzerConfigOptionsProvider" /> that answers only the global options
    ///     it was constructed with — enough to drive <c>build_property.RootNamespace</c>, the only
    ///     MSBuild property <c>EndpointsGenerator</c> reads. Modeled on
    ///     <c>Synapse.Generator.Tests.GeneratorBehaviorTests.TestAnalyzerConfigOptionsProvider</c>.
    /// </summary>
    private sealed class TestAnalyzerConfigOptionsProvider(Dictionary<string, string> globalOptions)
        : AnalyzerConfigOptionsProvider
    {
        private readonly TestAnalyzerConfigOptions _global = new(globalOptions);

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _global;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _global;
    }

    private sealed class TestAnalyzerConfigOptions(Dictionary<string, string> options) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string value)
        {
            if (options.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }

            value = null!;
            return false;
        }
    }
}
