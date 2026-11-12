using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Generator.Tests;

/// <summary>
///     The generator side of relative behavior ordering (#162): <c>[PipelineBehavior(Stage, Before, After)]</c> becomes
///     a <c>ConfigureBehaviorOrdering</c> call, and what the generator can prove wrong is a build error.
/// </summary>
public sealed class GeneratorBehaviorOrderingTests
{
    private const string Handlers = """
        using System.Threading;
        using System.Threading.Tasks;
        using UnambitiousFx.Functional;
        using UnambitiousFx.Synapse.Abstractions;

        namespace TestNs;

        public sealed record MyQuery : IRequest<int>;

        [RequestHandler<MyQuery, int>]
        public sealed class MyHandler : IRequestHandler<MyQuery, int>
        {
            public ValueTask<Result<int>> HandleAsync(MyQuery request, CancellationToken ct = default)
                => ValueTask.FromResult(Result.Success(1));
        }

        """;

    private static string Behavior(string name, string attribute)
    {
        return $$"""

            {{attribute}}
            public sealed class {{name}}<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : notnull
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
                    RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }

            """;
    }

    [Fact]
    public void Generate_WithStageAndAfter_EmitsTheOrderingRegistration()
    {
        // Arrange (Given)
        var source = Handlers +
                     Behavior("ActorBehavior", "[PipelineBehavior(Stage = PipelineStage.Authentication)]") +
                     Behavior("AuditBehavior",
                         "[PipelineBehavior(Stage = PipelineStage.Authorization, After = typeof(ActorBehavior<,>))]");

        // Act (When)
        var (diagnostics, generated, errors) = Run(source);

        // Assert (Then)
        Assert.Contains(
            "builder.ConfigureBehaviorOrdering(typeof(global::TestNs.AuditBehavior<,>), global::UnambitiousFx.Synapse.Abstractions.PipelineStage.Authorization, null, typeof(global::TestNs.ActorBehavior<,>));",
            generated);
        Assert.Contains(
            "builder.ConfigureBehaviorOrdering(typeof(global::TestNs.ActorBehavior<,>), global::UnambitiousFx.Synapse.Abstractions.PipelineStage.Authentication, null, null);",
            generated);
        Assert.DoesNotContain(diagnostics, d => d.Id is "MDG018" or "MDG019");
        Assert.Empty(errors);
    }

    [Fact]
    public void Generate_WithAClosedBeforeTarget_EmitsTheClosedType()
    {
        // Arrange (Given)
        var source = Handlers +
                     Behavior("ScopeBehavior", "[PipelineBehavior]") +
                     Behavior("AuditBehavior", "[PipelineBehavior(Before = typeof(ScopeBehavior<MyQuery, int>))]");

        // Act (When)
        var (_, generated, errors) = Run(source);

        // Assert (Then)
        Assert.Contains(
            "builder.ConfigureBehaviorOrdering(typeof(global::TestNs.AuditBehavior<,>), null, typeof(global::TestNs.ScopeBehavior<global::TestNs.MyQuery, int>), null);",
            generated);
        Assert.Empty(errors);
    }

    [Fact]
    public void Generate_WithAPlainAttribute_EmitsNoOrderingRegistration()
    {
        // Arrange (Given) — behaviors that only rely on Order generate exactly what they always did.
        var source = Handlers + Behavior("LoggingBehavior", "[PipelineBehavior]");

        // Act (When)
        var (_, generated, _) = Run(source);

        // Assert (Then)
        Assert.DoesNotContain("ConfigureBehaviorOrdering", generated);
    }

    [Fact]
    public void Generate_WithABeforeAfterCycle_ReportsMdg018OnEachBehavior()
    {
        // Arrange (Given)
        var source = Handlers +
                     Behavior("FirstBehavior", "[PipelineBehavior(Before = typeof(SecondBehavior<,>))]") +
                     Behavior("SecondBehavior", "[PipelineBehavior(Before = typeof(FirstBehavior<,>))]");

        // Act (When)
        var (diagnostics, _, _) = Run(source);

        // Assert (Then)
        var cycle = diagnostics.Where(d => d.Id == "MDG018").ToArray();
        Assert.Equal(2, cycle.Length);
        Assert.All(cycle, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.All(cycle, d => Assert.NotEqual(Location.None, d.Location));
        Assert.Contains("FirstBehavior<,>", cycle[0].GetMessage(), StringComparison.Ordinal);
        Assert.Contains("SecondBehavior<,>", cycle[0].GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithOppositeConstraintsOnDifferentClosedTargets_ReportsNoCycle()
    {
        // Arrange (Given) — "A before B<MyQuery, int>" and "B before A<OtherQuery, string>" never meet in one
        // pipeline, so they are not a cycle.
        var source = Handlers.Replace("public sealed record MyQuery : IRequest<int>;",
                         "public sealed record MyQuery : IRequest<int>;\npublic sealed record OtherQuery : IRequest<string>;",
                         StringComparison.Ordinal) +
                     Behavior("ABehavior", "[PipelineBehavior(Before = typeof(BBehavior<MyQuery, int>))]") +
                     Behavior("BBehavior", "[PipelineBehavior(Before = typeof(ABehavior<OtherQuery, string>))]");

        // Act (When)
        var (diagnostics, generated, errors) = Run(source);

        // Assert (Then)
        Assert.DoesNotContain(diagnostics, d => d.Id == "MDG018");
        Assert.Contains("typeof(global::TestNs.BBehavior<global::TestNs.MyQuery, int>)", generated);
        Assert.Empty(errors);
    }

    [Fact]
    public void Generate_WithOppositeConstraintsOnTheSameClosedTargets_ReportsMdg018()
    {
        // Arrange (Given) — both closed targets are over MyQuery/int, so that pipeline has both edges.
        var source = Handlers +
                     Behavior("ABehavior", "[PipelineBehavior(Before = typeof(BBehavior<MyQuery, int>))]") +
                     Behavior("BBehavior", "[PipelineBehavior(Before = typeof(ABehavior<MyQuery, int>))]");

        // Act (When)
        var (diagnostics, _, _) = Run(source);

        // Assert (Then)
        Assert.Equal(2, diagnostics.Count(d => d.Id == "MDG018"));
    }

    [Fact]
    public void Generate_WithAClosedAndAnOpenConstraintClosingACycle_ReportsMdg018()
    {
        // Arrange (Given) — the open edge applies to every pipeline, including MyQuery/int.
        var source = Handlers +
                     Behavior("ABehavior", "[PipelineBehavior(Before = typeof(BBehavior<MyQuery, int>))]") +
                     Behavior("BBehavior", "[PipelineBehavior(Before = typeof(ABehavior<,>))]");

        // Act (When)
        var (diagnostics, _, _) = Run(source);

        // Assert (Then)
        Assert.Equal(2, diagnostics.Count(d => d.Id == "MDG018"));
    }

    private static string ResultBehavior(string name, string attribute)
    {
        return $$"""

            {{attribute}}
            public sealed class {{name}}<TRequest, TResult> : IResultPipelineBehavior<TRequest, TResult>
                where TResult : IResult
            {
                public ValueTask<TResult> HandleAsync(TRequest request, RequestNext<TRequest, TResult> next,
                    CancellationToken ct = default)
                    => next.InvokeAsync(request, ct);
            }

            """;
    }

    [Fact]
    public void Generate_WithACycleBetweenATypedAndAResultBehaviorOfOnePipeline_ReportsMdg018()
    {
        // Arrange (Given) — Typed<MyQuery, int> and Result<MyQuery, Result<int>> close MyQuery's pipeline with
        // different type arguments, yet both constraints apply to it.
        var source = Handlers +
                     Behavior("TypedBehavior",
                         "[PipelineBehavior(Before = typeof(ResultTraceBehavior<MyQuery, Result<int>>))]") +
                     ResultBehavior("ResultTraceBehavior",
                         "[PipelineBehavior(Before = typeof(TypedBehavior<MyQuery, int>))]");

        // Act (When)
        var (diagnostics, _, _) = Run(source);

        // Assert (Then)
        Assert.Equal(2, diagnostics.Count(d => d.Id == "MDG018"));
    }

    [Fact]
    public void Generate_WithTypedAndResultConstraintsOnDifferentPipelines_ReportsNoCycle()
    {
        // Arrange (Given) — the Result target is closed over OtherQuery, the typed one over MyQuery.
        var source = Handlers.Replace("public sealed record MyQuery : IRequest<int>;",
                         "public sealed record MyQuery : IRequest<int>;\npublic sealed record OtherQuery : IRequest<string>;",
                         StringComparison.Ordinal) +
                     Behavior("TypedBehavior",
                         "[PipelineBehavior(Before = typeof(ResultTraceBehavior<OtherQuery, Result<string>>))]") +
                     ResultBehavior("ResultTraceBehavior",
                         "[PipelineBehavior(Before = typeof(TypedBehavior<MyQuery, int>))]");

        // Act (When)
        var (diagnostics, _, errors) = Run(source);

        // Assert (Then)
        Assert.DoesNotContain(diagnostics, d => d.Id == "MDG018");
        Assert.Empty(errors);
    }

    [Fact]
    public void Generate_WithALongerCycle_ReportsMdg018()
    {
        // Arrange (Given) — A after C, B after A, C after B.
        var source = Handlers +
                     Behavior("ABehavior", "[PipelineBehavior(After = typeof(CBehavior<,>))]") +
                     Behavior("BBehavior", "[PipelineBehavior(After = typeof(ABehavior<,>))]") +
                     Behavior("CBehavior", "[PipelineBehavior(After = typeof(BBehavior<,>))]");

        // Act (When)
        var (diagnostics, _, _) = Run(source);

        // Assert (Then)
        Assert.Equal(3, diagnostics.Count(d => d.Id == "MDG018"));
    }

    [Fact]
    public void Generate_WithAConstraintThatContradictsTheStages_ReportsMdg019()
    {
        // Arrange (Given) — a Validation behavior cannot run before an Observability one.
        var source = Handlers +
                     Behavior("TraceBehavior", "[PipelineBehavior(Stage = PipelineStage.Observability)]") +
                     Behavior("CheckBehavior",
                         "[PipelineBehavior(Stage = PipelineStage.Validation, Before = typeof(TraceBehavior<,>))]");

        // Act (When)
        var (diagnostics, _, _) = Run(source);

        // Assert (Then)
        var issue = Assert.Single(diagnostics, d => d.Id == "MDG019");
        Assert.Equal(DiagnosticSeverity.Error, issue.Severity);
        Assert.Contains("Validation stage, after Observability", issue.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithABehaviorOrderedAfterItself_ReportsMdg019()
    {
        // Arrange (Given)
        var source = Handlers + Behavior("SelfBehavior", "[PipelineBehavior(After = typeof(SelfBehavior<,>))]");

        // Act (When)
        var (diagnostics, _, _) = Run(source);

        // Assert (Then)
        var issue = Assert.Single(diagnostics, d => d.Id == "MDG019");
        Assert.Contains("itself", issue.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_WithAStagedGlobalBehaviorFromAReference_EmitsItsOrdering()
    {
        // Arrange (Given) — a packaged behavior declares its stage on its own [PipelineBehavior].
        const string package = """
            using System.Threading;
            using System.Threading.Tasks;
            using UnambitiousFx.Functional;
            using UnambitiousFx.Synapse.Abstractions;

            namespace Package;

            [PipelineBehavior(Stage = PipelineStage.Transaction)]
            public sealed class TransactionBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : notnull
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
                    RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }
            """;
        var source = Handlers.Replace("namespace TestNs;",
            "[assembly: SynapseGlobalBehavior(typeof(Package.TransactionBehavior<,>))]\n\nnamespace TestNs;",
            StringComparison.Ordinal);

        // Act (When)
        var (_, generated, errors) = Run(source, package);

        // Assert (Then)
        Assert.Contains(
            "builder.ConfigureBehaviorOrdering(typeof(global::Package.TransactionBehavior<,>), global::UnambitiousFx.Synapse.Abstractions.PipelineStage.Transaction, null, null);",
            generated);
        Assert.Empty(errors);
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string Generated, Diagnostic[] CompileErrors) Run(
        string source, string? referencedSource = null)
    {
        var references = GetMetadataReferences().ToList();
        if (referencedSource is not null)
        {
            references.Add(CSharpCompilation.Create(
                    "PackageAssembly",
                    [CSharpSyntaxTree.ParseText(referencedSource)],
                    GetMetadataReferences(),
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                        nullableContextOptions: NullableContextOptions.Enable))
                .ToMetadataReference());
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new SynapseGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult();
        var generated = result.GeneratedTrees
            .FirstOrDefault(tree => tree.FilePath.EndsWith("RegisterGroup.g.cs", StringComparison.Ordinal))
            ?.GetText().ToString() ?? string.Empty;
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        return (result.Diagnostics, generated, errors);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var trustedPaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return trustedPaths
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(PipelineBehaviorAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Functional.Result).Assembly.Location));
    }
}
