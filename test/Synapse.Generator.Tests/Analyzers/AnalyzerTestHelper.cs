using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

/// <summary>
///     Runs one analyzer over minimal C# source. No analyzer-testing package is available, so this builds the
///     compilation by hand, like the generator tests do.
/// </summary>
internal static class AnalyzerTestHelper
{
    public const string Preamble = """
        using System.Threading;
        using System.Threading.Tasks;
        using UnambitiousFx.Functional;
        using UnambitiousFx.Synapse.Abstractions;

        """;

    private static readonly CSharpCompilationOptions Options =
        new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

    public static Task<ImmutableArray<Diagnostic>> RunAsync<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        return RunCoreAsync<TAnalyzer>(source, "Test.cs", null);
    }

    public static Task<ImmutableArray<Diagnostic>> RunAtPathAsync<TAnalyzer>(string source, string path)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        return RunCoreAsync<TAnalyzer>(source, path, null);
    }

    public static Task<ImmutableArray<Diagnostic>> RunWithReferenceAsync<TAnalyzer>(string referencedSource,
        string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        return RunCoreAsync<TAnalyzer>(source, "Test.cs", referencedSource);
    }

    private static async Task<ImmutableArray<Diagnostic>> RunCoreAsync<TAnalyzer>(string source, string path,
        string? referencedSource)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var references = GetMetadataReferences().ToList();
        if (referencedSource is not null)
        {
            var referenced = CSharpCompilation.Create("ReferencedAssembly",
                [CSharpSyntaxTree.ParseText(referencedSource)], references, Options);
            references.Add(referenced.ToMetadataReference());
        }

        var compilation = CSharpCompilation.Create("TestAssembly",
            [CSharpSyntaxTree.ParseText(source, path: path)], references, Options);

        var withAnalyzers = compilation.WithAnalyzers([new TAnalyzer()]);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var trustedPaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var references = trustedPaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(PipelineBehaviorAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(UnambitiousFx.Functional.Result).Assembly.Location));
        return references;
    }
}
