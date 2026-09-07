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
        foreach (var file in GetFiles(source))
        {
            if (file.Key.EndsWith(fileName, StringComparison.Ordinal))
            {
                return file.Value;
            }
        }

        return null;
    }

    /// <summary>
    ///     Every file the generator emitted for <paramref name="source" />, keyed by hint name.
    /// </summary>
    /// <param name="source">The source to run the generator over.</param>
    /// <returns>The generated files, keyed by hint name.</returns>
    /// <remarks>
    ///     One generator run serves the whole dictionary, so a test asserting about two files does not
    ///     compile the same source twice — the harness's dominant cost.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> GetFiles(string source)
    {
        return ToFileMap(Run([source]).Trees);
    }

    /// <summary>
    ///     Every file the generator emitted for <paramref name="sources" />, keyed by hint name — the
    ///     multi-file form, for the shapes that only exist across files (an endpoint declared in two
    ///     parts, for one).
    /// </summary>
    /// <param name="sources">The sources to compile together and run the generator over.</param>
    /// <returns>The generated files, keyed by hint name.</returns>
    internal static IReadOnlyDictionary<string, string> GetFilesFromSources(params string[] sources)
    {
        return ToFileMap(Run(sources).Trees);
    }

    private static IReadOnlyDictionary<string, string> ToFileMap(IReadOnlyList<SyntaxTree> trees)
    {
        return trees.ToDictionary(
            static tree => Path.GetFileName(tree.FilePath),
            static tree => tree.ToString(),
            StringComparer.Ordinal);
    }

    /// <summary>
    ///     Returns the single per-endpoint generated file. Most emission tests declare one endpoint, so
    ///     they need not repeat its hint name.
    /// </summary>
    /// <param name="source">The source to run the generator over.</param>
    /// <returns>The generated file's text.</returns>
    internal static string GetEndpointFile(string source)
    {
        return SingleEndpointFile(GetFiles(source));
    }

    /// <summary>
    ///     Same as <see cref="GetEndpointFile" />, but with additional metadata references, so a test
    ///     can put the message type in a referenced assembly.
    /// </summary>
    /// <param name="source">The source to run the generator over.</param>
    /// <param name="extraReferences">References to add to the compilation.</param>
    /// <returns>The generated file's text.</returns>
    internal static string GetEndpointFileWithReferences(string source,
        params MetadataReference[] extraReferences)
    {
        return SingleEndpointFile(ToFileMap(Run([source], extraReferences: extraReferences).Trees));
    }

    private static string SingleEndpointFile(IReadOnlyDictionary<string, string> files)
    {
        var endpointFiles = files
            .Where(static file => file.Key.EndsWith(".Synapse.g.cs", StringComparison.Ordinal))
            .ToArray();

        return endpointFiles.Length == 1
            ? endpointFiles[0].Value
            : throw new InvalidOperationException(
                $"Expected exactly one per-endpoint generated file, found {endpointFiles.Length}: " +
                string.Join(", ", endpointFiles.Select(static file => file.Key)));
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

        var tree = Run([source], provider).Trees
            .FirstOrDefault(t => t.FilePath.EndsWith(fileName, StringComparison.Ordinal));
        return tree?.ToString()
               ?? throw new InvalidOperationException($"The generator did not emit '{fileName}'.");
    }

    /// <summary>
    ///     Same as <see cref="GetFile" />, but with additional metadata references, so a test can put
    ///     the message type in a referenced assembly — the case that decides whether an
    ///     <c>internal</c> constructor is actually callable from the generated binder.
    /// </summary>
    internal static string GetFileWithReferences(string source, string fileName,
        params MetadataReference[] extraReferences)
    {
        var tree = Run([source], extraReferences: extraReferences).Trees
            .FirstOrDefault(t => t.FilePath.EndsWith(fileName, StringComparison.Ordinal));

        return tree?.ToString()
               ?? throw new InvalidOperationException($"The generator did not emit '{fileName}'.");
    }

    /// <summary>
    ///     Same as <see cref="AssertGeneratedCompiles(string)" />, but with additional metadata
    ///     references.
    /// </summary>
    internal static void AssertGeneratedCompilesWithReferences(string source,
        params MetadataReference[] extraReferences)
    {
        AssertGeneratedCompiles([source], optionsProvider: null, allowGeneratorErrors: false,
            extraReferences: extraReferences);
    }

    internal static ImmutableArray<Diagnostic> GetDiagnostics(string source)
    {
        return Run([source]).Diagnostics;
    }

    /// <summary>
    ///     Same as <see cref="GetDiagnostics(string)" />, but over several sources compiled together
    ///     — for the shapes that only exist across files, such as an endpoint declared in two parts.
    /// </summary>
    /// <param name="sources">The sources to compile together and run the generator over.</param>
    /// <returns>What the generator reported.</returns>
    internal static ImmutableArray<Diagnostic> GetDiagnosticsFromSources(params string[] sources)
    {
        return Run(sources).Diagnostics;
    }

    /// <summary>
    ///     Same as <see cref="GetDiagnostics(string)" />, but with additional metadata references —
    ///     used to exercise SYNE008's reference-graph scan, where the <c>JsonSerializerContext</c>
    ///     under test lives in a separately compiled assembly rather than <paramref name="source" />
    ///     itself. See <see cref="CompileToReference" />.
    /// </summary>
    internal static ImmutableArray<Diagnostic> GetDiagnostics(string source, params MetadataReference[] extraReferences)
    {
        return Run([source], extraReferences: extraReferences).Diagnostics;
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
        AssertGeneratedCompiles([source], optionsProvider: null, allowGeneratorErrors: false);
    }

    /// <summary>
    ///     Same as <see cref="AssertGeneratedCompiles(string)" />, but over several sources compiled
    ///     together — for the shapes that only exist across files, such as an endpoint declared in
    ///     two parts.
    /// </summary>
    /// <param name="sources">The sources to compile together and run the generator over.</param>
    internal static void AssertGeneratedCompilesFromSources(params string[] sources)
    {
        AssertGeneratedCompiles(sources, optionsProvider: null, allowGeneratorErrors: false);
    }

    /// <summary>
    ///     Same as <see cref="AssertGeneratedCompiles(string)" />, but tolerating the generator's own
    ///     Error-severity diagnostics — for the tests whose subject <em>is</em> a SYNEnnn error and
    ///     which additionally want to pin that what is still emitted around the omitted property
    ///     compiles. Named so the exemption is visible at the call site, because it is exactly the
    ///     exemption that let a build-breaking SYNE012 ship unnoticed.
    /// </summary>
    internal static void AssertGeneratedCompilesDespiteDiagnostics(string source)
    {
        AssertGeneratedCompiles([source], optionsProvider: null, allowGeneratorErrors: true);
    }

    /// <summary>
    ///     The generator's own diagnostics, which <c>Compilation.GetDiagnostics()</c> never sees: it
    ///     reports C# diagnostics about the updated compilation, not what the driver reported while
    ///     producing it. Discarding the driver's <c>out</c> parameter therefore let a test assert
    ///     "the generated code compiles" about a source the generator had refused to bind a property
    ///     of, reporting a build-breaking Error as it did so.
    /// </summary>
    private static void AssertNoGeneratorErrors(ImmutableArray<Diagnostic> generatorDiagnostics)
    {
        var reported = generatorDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(reported.Length == 0,
            "The generator reported errors: " + string.Join("; ", reported.Select(e => e.ToString())));
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
        AssertGeneratedCompiles([source],
            new TestAnalyzerConfigOptionsProvider(
                new Dictionary<string, string> { ["build_property.RootNamespace"] = rootNamespace }),
            allowGeneratorErrors: false);
    }

    private static void AssertGeneratedCompiles(IReadOnlyList<string> sources,
        AnalyzerConfigOptionsProvider? optionsProvider,
        bool allowGeneratorErrors,
        IReadOnlyList<MetadataReference>? extraReferences = null)
    {
        var run = Run(sources, optionsProvider, extraReferences);

        if (!allowGeneratorErrors)
        {
            AssertNoGeneratorErrors(run.Diagnostics);
        }

        var errors = run.Updated.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(errors.Length == 0,
            "Generated code should compile, but got: " + string.Join("; ", errors.Select(e => e.ToString())));

        // A generator that silently emits nothing (e.g. because base-type matching failed for a
        // shape) introduces no diagnostics and would otherwise pass the checks above unnoticed.
        Assert.True(run.Trees.Count > 0, "The generator produced no source at all.");
    }

    /// <summary>
    ///     The one place the generator driver is set up and run. Every harness entry point goes
    ///     through it, so the CS8785 guard below cannot be missing from some of them — which is
    ///     exactly how a generator that crashed on a multi-part endpoint declaration went unnoticed:
    ///     three of the four paths used to build their own driver and skip the check.
    /// </summary>
    /// <param name="sources">The sources to compile together.</param>
    /// <param name="optionsProvider">The analyzer config options, or null to leave them unset.</param>
    /// <param name="extraReferences">References to add on top of the shared framework set.</param>
    /// <returns>What the generator reported, what it emitted, and the compilation it emitted into.</returns>
    private static GeneratorRun Run(IReadOnlyList<string> sources,
        AnalyzerConfigOptionsProvider? optionsProvider = null,
        IReadOnlyList<MetadataReference>? extraReferences = null)
    {
        var compilation = CreateCompilation(sources);
        if (extraReferences is { Count: > 0 })
        {
            compilation = compilation.AddReferences(extraReferences);
        }

        var driver = optionsProvider is null
            ? CSharpGeneratorDriver.Create(new EndpointsGenerator())
            : CSharpGeneratorDriver.Create([new EndpointsGenerator().AsSourceGenerator()],
                optionsProvider: optionsProvider);
        var updatedDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated,
            out var generatorDiagnostics);

        // A generator that throws mid-Analyze — or that calls AddSource twice with one hint name —
        // surfaces as CS8785, which the compiler treats as a Warning by default. Checking only
        // Error-severity diagnostics would therefore let a crashing generator through silently, so
        // CS8785 is fatal here regardless of the severity the driver assigned it.
        var generatorFailures = generatorDiagnostics
            .Where(d => d.Id == "CS8785")
            .ToArray();

        Assert.True(generatorFailures.Length == 0,
            "The generator itself threw: " + string.Join("; ", generatorFailures.Select(e => e.ToString())));

        var result = updatedDriver.GetRunResult();
        return new GeneratorRun(result.Diagnostics, result.GeneratedTrees, updated);
    }

    private static CSharpCompilation CreateCompilation(IReadOnlyList<string> sources)
    {
        return CSharpCompilation.Create(
            "TestAssembly",
            sources.Select(static source => CSharpSyntaxTree.ParseText(source)),
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    /// <summary>One generator run: what it reported, what it emitted, and the updated compilation.</summary>
    private readonly record struct GeneratorRun(
        ImmutableArray<Diagnostic> Diagnostics,
        IReadOnlyList<SyntaxTree> Trees,
        Compilation Updated);

    /// <summary>
    ///     The framework reference set, built once per process.
    /// </summary>
    /// <remarks>
    ///     Built once, not per compilation. <see cref="MetadataReference.CreateFromFile(string, MetadataReferenceProperties, DocumentationProvider)" />
    ///     has no cache: every call re-reads the file and builds a fresh <c>AssemblyMetadata</c> holding a
    ///     memory-mapped image of it. TRUSTED_PLATFORM_ASSEMBLIES here is 313 assemblies and 94 MB, and
    ///     this harness compiles once per test, so rebuilding the list per call cost tens of thousands of
    ///     mapped images per run and exhausted system memory — it OOM-killed the host twice. Roslyn's
    ///     reference objects are immutable and designed to be shared across compilations, so one list
    ///     serves every test.
    /// </remarks>
    private static readonly ImmutableArray<MetadataReference> SharedMetadataReferences = BuildMetadataReferences();

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
        return SharedMetadataReferences;
    }

    private static ImmutableArray<MetadataReference> BuildMetadataReferences()
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

        return refs.ToImmutableArray();
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
