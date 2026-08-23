using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        var (_, trees) = Run(source);
        var tree = trees.FirstOrDefault(t => t.FilePath.EndsWith(fileName, StringComparison.Ordinal));
        return tree?.ToString();
    }

    internal static ImmutableArray<Diagnostic> GetDiagnostics(string source)
    {
        var (diagnostics, _) = Run(source);
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
    ///     handle.
    /// </summary>
    internal static MetadataReference CompileToReference(string source, string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            GetMetadataReferences(),
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
        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new EndpointsGenerator());
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

    private static (ImmutableArray<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> Trees) Run(string source)
    {
        var driver = CSharpGeneratorDriver.Create(new EndpointsGenerator());
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
    ///     emitted <c>EndpointGroup.g.cs</c> names <c>Microsoft.AspNetCore.Routing.IEndpointRouteBuilder</c>.
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
}
