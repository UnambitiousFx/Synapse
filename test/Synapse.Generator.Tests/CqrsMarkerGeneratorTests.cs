using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Generator;

namespace UnambitiousFx.Synapse.Generator.Tests;

/// <summary>
///     The source generator must treat ICommand / IQuery requests and their handler aliases like any request, and
///     scope a behavior by its generic constraint.
/// </summary>
public sealed class CqrsMarkerGeneratorTests
{
    private const string UsingsOnly = """
        using System.Threading;
        using System.Threading.Tasks;
        using UnambitiousFx.Functional;
        using UnambitiousFx.Synapse.Abstractions;

        """;

    private const string NamespaceLine = "namespace TestNs;\n\n";

    private const string Usings = UsingsOnly + NamespaceLine;

    private const string Types = """
        public sealed record CreateCommand : ICommand<int>;

        public sealed record GetQuery : IQuery<int>;

        [RequestHandler<CreateCommand, int>]
        public sealed class CreateHandler : ICommandHandler<CreateCommand, int>
        {
            public ValueTask<Result<int>> HandleAsync(CreateCommand request, CancellationToken ct = default)
                => ValueTask.FromResult(Result.Success(1));
        }

        [RequestHandler<GetQuery, int>]
        public sealed class GetHandler : IQueryHandler<GetQuery, int>
        {
            public ValueTask<Result<int>> HandleAsync(GetQuery request, CancellationToken ct = default)
                => ValueTask.FromResult(Result.Success(2));
        }

        public sealed class TransactionBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
            where TRequest : ICommand<TResponse>
            where TResponse : notnull
        {
            public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
                RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                => next(request, ct);
        }
        """;

    [Fact]
    public void Generate_WithHandlersImplementingTheAliases_EmitsTheirRegistrationsAndCompiles()
    {
        // Arrange (Given)
        var source = Usings + Types;

        // Act (When)
        var (generated, errors) = Run(source);

        // Assert (Then)
        Assert.Contains("RegisterRequestHandler<global::TestNs.CreateHandler, global::TestNs.CreateCommand, int>()",
            generated);
        Assert.Contains("RegisterRequestHandler<global::TestNs.GetHandler, global::TestNs.GetQuery, int>()",
            generated);
        Assert.Empty(errors);
    }

    [Fact]
    public void Generate_WithAPipelineBehaviorConstrainedToCommands_ClosesItOverCommandHandlersOnly()
    {
        // Arrange (Given)
        var source = Usings + Types.Replace("public sealed class TransactionBehavior",
            "[PipelineBehavior] public sealed class TransactionBehavior");

        // Act (When)
        var (generated, errors) = Run(source);

        // Assert (Then)
        Assert.Contains("TransactionBehavior<global::TestNs.CreateCommand, int>", generated);
        Assert.DoesNotContain("TransactionBehavior<global::TestNs.GetQuery", generated);
        Assert.Empty(errors);
    }

    [Fact]
    public void Generate_WithAGlobalBehaviorConstrainedToCommands_ClosesItOverCommandHandlersOnly()
    {
        // Arrange (Given)
        // A using directive must precede the assembly attribute.
        var source = UsingsOnly
                     + "[assembly: SynapseGlobalBehavior(typeof(TestNs.TransactionBehavior<,>))]\n\n"
                     + NamespaceLine
                     + Types;

        // Act (When)
        var (generated, errors) = Run(source);

        // Assert (Then)
        Assert.Contains("TransactionBehavior<global::TestNs.CreateCommand, int>", generated);
        Assert.DoesNotContain("TransactionBehavior<global::TestNs.GetQuery", generated);
        Assert.Empty(errors);
    }

    private static (string Generated, ImmutableArray<Diagnostic> Errors) Run(string source)
    {
        var compilation = CSharpCompilation.Create("TestAssembly", [CSharpSyntaxTree.ParseText(source)],
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new SynapseGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        var generated = driver.GetRunResult().GeneratedTrees
            .FirstOrDefault(tree => tree.FilePath.EndsWith("RegisterGroup.g.cs", StringComparison.Ordinal))?
            .GetText().ToString() ?? string.Empty;
        var errors = updated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        return (generated, errors);
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
