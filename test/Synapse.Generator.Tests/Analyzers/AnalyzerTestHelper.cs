using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse;
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
        return RunCoreAsync<TAnalyzer>([("Test.cs", source)], null);
    }

    public static Task<ImmutableArray<Diagnostic>> RunAtPathAsync<TAnalyzer>(string source, string path)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        return RunCoreAsync<TAnalyzer>([(path, source)], null);
    }

    public static Task<ImmutableArray<Diagnostic>> RunFilesAsync<TAnalyzer>(
        params (string Path, string Source)[] files)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        return RunCoreAsync<TAnalyzer>(files, null);
    }

    public static Task<ImmutableArray<Diagnostic>> RunWithReferenceAsync<TAnalyzer>(string referencedSource,
        string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        return RunCoreAsync<TAnalyzer>([("Test.cs", source)], referencedSource);
    }

    private static async Task<ImmutableArray<Diagnostic>> RunCoreAsync<TAnalyzer>(
        (string Path, string Source)[] files, string? referencedSource)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var references = GetMetadataReferences().ToList();
        if (referencedSource is not null)
        {
            var referenced = CSharpCompilation.Create("ReferencedAssembly",
                [CSharpSyntaxTree.ParseText(referencedSource)], references, Options);
            AssertCompiles(referenced);
            references.Add(referenced.ToMetadataReference());
        }

        var compilation = CSharpCompilation.Create("TestAssembly",
            files.Select(file => CSharpSyntaxTree.ParseText(file.Source, path: file.Path)), references, Options);
        AssertCompiles(compilation);

        var withAnalyzers = compilation.WithAnalyzers([new TAnalyzer()]);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }

    private static void AssertCompiles(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();
        Assert.True(errors.Count == 0, "The test fixture does not compile:" + Environment.NewLine +
                                       string.Join(Environment.NewLine, errors));
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
        references.Add(MetadataReference.CreateFromFile(typeof(ISynapseConfig).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location));
        return references;
    }
}
