namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     The declaration wrapper around a generated binding: namespace, nesting, modifiers, hint name,
///     and the fact that two endpoints sharing a message each get their own binding.
/// </summary>
public sealed class PartialEmissionTests
{
    private const string TopLevel = """
                                    using UnambitiousFx.Synapse.Abstractions;
                                    using UnambitiousFx.Synapse.Endpoints;

                                    namespace TestNs;

                                    public sealed record ProbeQuery : IRequest<string>
                                    {
                                        public string Name { get; init; } = "";
                                    }

                                    [Get("/probes/{name}")]
                                    public sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>;
                                    """;

    private const string StreamTier = """
                                      using UnambitiousFx.Synapse.Abstractions;
                                      using UnambitiousFx.Synapse.Endpoints;

                                      namespace TestNs;

                                      public sealed record ProbeStream : IStreamRequest<string>;

                                      [Get("/probes")]
                                      public sealed partial class ProbeEndpoint
                                          : StreamEndpoint<ProbeStream, string>;
                                      """;

    private const string MappedTier = """
                                      using UnambitiousFx.Synapse.Abstractions;
                                      using UnambitiousFx.Synapse.Endpoints;

                                      namespace TestNs;

                                      public sealed record ProbeDto(string Name);
                                      public sealed record ProbeQuery(string Name) : IRequest<string>;

                                      [Get("/probes/{name}")]
                                      public sealed partial class ProbeEndpoint
                                          : ContractEndpoint<ProbeDto, ProbeQuery, string, string>
                                      {
                                          public override ProbeQuery ToRequest(ProbeDto request)
                                              => new(request.Name);

                                          public override string ToResponse(string response)
                                              => response;
                                      }
                                      """;

    private const string MappedVoidTier = """
                                          using UnambitiousFx.Synapse.Abstractions;
                                          using UnambitiousFx.Synapse.Endpoints;

                                          namespace TestNs;

                                          public sealed record ProbeDto(string Name);
                                          public sealed record ProbeCommand(string Name) : IRequest;

                                          [Delete("/probes/{name}")]
                                          public sealed partial class ProbeEndpoint
                                              : ContractEndpoint<ProbeDto, ProbeCommand>
                                          {
                                              public override ProbeCommand ToRequest(ProbeDto request)
                                                  => new(request.Name);
                                          }
                                          """;

    private const string InlineTier = """
                                           using System.Threading;
                                           using System.Threading.Tasks;
                                           using Microsoft.AspNetCore.Http;
                                           using UnambitiousFx.Functional;
                                           using UnambitiousFx.Synapse.Abstractions;
                                           using UnambitiousFx.Synapse.Endpoints;

                                           namespace TestNs;

                                           public sealed record ProbeQuery : IRequest<string>;

                                           [Get("/probes")]
                                           public sealed partial class ProbeEndpoint
                                               : InlineEndpoint<ProbeQuery, string>
                                           {
                                               public override ValueTask<Result<string>> ExecuteAsync(
                                                   ProbeQuery request,
                                                   HttpContext context,
                                                   CancellationToken cancellationToken)
                                                   => new(Result.Success("ok"));
                                           }
                                           """;

    private const string InlineVoidTier = """
                                               using System.Threading;
                                               using System.Threading.Tasks;
                                               using Microsoft.AspNetCore.Http;
                                               using UnambitiousFx.Functional;
                                               using UnambitiousFx.Synapse.Abstractions;
                                               using UnambitiousFx.Synapse.Endpoints;

                                               namespace TestNs;

                                               public sealed record ProbeCommand : IRequest;

                                               [Post("/probes")]
                                               public sealed partial class ProbeEndpoint
                                                   : InlineEndpoint<ProbeCommand>
                                               {
                                                   public override ValueTask<Result> ExecuteAsync(
                                                       ProbeCommand request,
                                                       HttpContext context,
                                                       CancellationToken cancellationToken)
                                                       => new(Result.Success());
                                               }
                                               """;

    [Fact]
    public void Generate_ForTopLevelEndpoint_ReopensTheClassInItsOwnNamespace()
    {
        // Arrange — see TopLevel.

        // Act
        var files = GeneratorHarness.GetFiles(TopLevel);

        // Assert — the endpoint's own namespace, not the root namespace, and no companion file.
        var generated = files["TestNs.ProbeEndpoint.Synapse.g.cs"];
        Assert.Contains("namespace TestNs;", generated);
        Assert.Contains("partial class ProbeEndpoint", generated);
        Assert.DoesNotContain("SynapseEndpointBinders.g.cs", files.Keys);
        GeneratorHarness.AssertGeneratedCompiles(TopLevel);
    }

    [Fact]
    public void Generate_ForSealedEndpoint_EmitsPlainOverrideNotSealedOverride()
    {
        // Arrange — ProbeEndpoint is declared sealed.

        // Act
        var generated = GeneratorHarness.GetEndpointFile(TopLevel);

        // Assert — `sealed override` inside a sealed class is redundant; emit `override`.
        Assert.Contains("public override", generated);
        Assert.DoesNotContain("sealed override", generated);
    }

    [Fact]
    public void Generate_ForUnsealedEndpoint_EmitsSealedOverrideSoItCannotBeOverriddenAgain()
    {
        // Arrange — concrete but not sealed, which is the only other shape that reaches the emitter:
        // SYNE010 rejects an abstract endpoint, since MapEndpoint<TEndpoint> has a new() constraint.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ProbeQuery : IRequest<string>;

                              [Get("/probes")]
                              public partial class ProbeEndpoint : Endpoint<ProbeQuery, string>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

        // Assert — generated members are not extension points, so a derivable endpoint seals them.
        Assert.Contains("public sealed override", generated);
        Assert.Contains("protected sealed override", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForNestedEndpoint_ReopensTheEnclosingTypeChain()
    {
        // Arrange — AssertGeneratedCompiles is what pins the nesting, and it is load-bearing rather
        // than a formality: a `partial class ProbeEndpoint` emitted at namespace scope declares a
        // *different* class, so the nested endpoint would be left with RawEndpoint's abstract
        // BindAsync unimplemented (CS0534) and this source would not compile.
        //
        // Both nested types are internal, not private. That is not a limitation of this emitter:
        // SynapseEndpointGroup.g.cs is still emitted at namespace scope and names every endpoint
        // type, so a private endpoint is unreachable from it (CS0122) — and an internal endpoint may
        // not derive from Endpoint<TPrivate, string>
        // either, because C# requires a base type to be at least as accessible as the class
        // (CS9338). Task 3's widening of ~86 nested test fixtures therefore stands.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public static partial class Outer
                              {
                                  internal sealed record ProbeQuery : IRequest<string>;

                                  [Get("/probes")]
                                  internal sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>;
                              }
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

        // Assert
        Assert.Contains("partial class Outer", generated);
        Assert.Contains("partial class ProbeEndpoint", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Theory]
    [InlineData("record", "record")]
    [InlineData("struct", "struct")]
    [InlineData("record struct", "record struct")]
    [InlineData("interface", "interface")]
    public void Generate_ForEndpointNestedInANonClassType_ReopensItWithItsOwnKeyword(string declaration,
        string keyword)
    {
        // Arrange — partial declarations of one type must all use the same keyword (CS0261), so
        // reopening a record or a struct with `partial class` is uncompilable generated code in a
        // file the user cannot edit. The pre-partial top-level binder never had to care, because it
        // named the enclosing type rather than reopening it.
        var source = $$"""
                       using UnambitiousFx.Synapse.Abstractions;
                       using UnambitiousFx.Synapse.Endpoints;

                       namespace TestNs;

                       public partial {{declaration}} Outer
                       {
                           [Get("/probes")]
                           internal sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>;
                       }

                       public sealed record ProbeQuery : IRequest<string>;
                       """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

        // Assert
        Assert.Contains($"partial {keyword} Outer", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForEndpointInGlobalNamespace_EmitsNoNamespaceDeclaration()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              public sealed record ProbeQuery : IRequest<string>;

                              [Get("/probes")]
                              public sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

        // Assert — `namespace ;` was the bug the root-namespace fallback exists to avoid.
        Assert.DoesNotContain("namespace", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForTwoEndpointsSharingOneMessage_EmitsIndependentBindingsAndNoSyne013()
    {
        // Arrange — the same message bound by a route parameter in one endpoint and by query in the
        // other. This is exactly the shape SYNE013 used to warn about.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ProbeQuery : IRequest<string>
                              {
                                  public string Name { get; init; } = "";
                              }

                              [Get("/by-route/{name}")]
                              public sealed partial class RouteProbeEndpoint : Endpoint<ProbeQuery, string>;

                              [Get("/by-query")]
                              public sealed partial class QueryProbeEndpoint : Endpoint<ProbeQuery, string>;
                              """;

        // Act
        var files = GeneratorHarness.GetFiles(source);
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert — one file each, each binding from its own source, and the warning is gone.
        Assert.Contains("TestNs.RouteProbeEndpoint.Synapse.g.cs", files.Keys);
        Assert.Contains("TestNs.QueryProbeEndpoint.Synapse.g.cs", files.Keys);
        Assert.Contains("TryGetRoute", files["TestNs.RouteProbeEndpoint.Synapse.g.cs"]);
        Assert.Contains("TryGetQuery", files["TestNs.QueryProbeEndpoint.Synapse.g.cs"]);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE013");
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(" : Endpoint<ProbeQuery, string>", "")]
    [InlineData(" : System.IDisposable", "public void Dispose() { }")]
    public void Generate_ForEndpointDeclaredInTwoParts_EmitsExactlyOneFile(string secondPartBaseList,
        string secondPartBody)
    {
        // Arrange — one endpoint, two declaration parts, in two files. Discovery matches every class
        // declaration carrying a base list, so the two parts that repeat the base class or add an
        // interface are each analysed separately and resolve to the same symbol — and the same hint
        // name. AddSource rejects a repeated hint name, which aborts the generator for the whole
        // compilation as a mere CS8785 *warning*, leaving every endpoint in the assembly without its
        // partial and the build failing on CS0534 instead.
        const string firstPart = """
                                 using UnambitiousFx.Synapse.Abstractions;
                                 using UnambitiousFx.Synapse.Endpoints;

                                 namespace TestNs;

                                 public sealed record ProbeQuery : IRequest<string>
                                 {
                                     public string Name { get; init; } = "";
                                 }

                                 [Get("/probes/{name}")]
                                 public sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>;
                                 """;

        var secondPart = $$"""
                           using UnambitiousFx.Synapse.Endpoints;

                           namespace TestNs;

                           partial class ProbeEndpoint{{secondPartBaseList}}
                           {
                               {{secondPartBody}}
                           }
                           """;

        // Act
        var files = GeneratorHarness.GetFilesFromSources(firstPart, secondPart);

        // Assert — exactly one partial for the endpoint, and it compiles against both parts.
        var endpointFiles = files.Keys
            .Where(static key => key.EndsWith(".Synapse.g.cs", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(["TestNs.ProbeEndpoint.Synapse.g.cs"], endpointFiles);
        GeneratorHarness.AssertGeneratedCompilesFromSources(firstPart, secondPart);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(" : Endpoint<ProbeQuery, string>", "")]
    public void Generate_ForEndpointDeclaredInTwoParts_ReportsEachDiagnosticOnce(string secondPartBaseList,
        string secondPartBody)
    {
        // Arrange — the same two-part shape, but with a route parameter no property matches, so one
        // SYNE001 is due. Reported once per matching declaration part, it would arrive twice.
        const string firstPart = """
                                 using UnambitiousFx.Synapse.Abstractions;
                                 using UnambitiousFx.Synapse.Endpoints;

                                 namespace TestNs;

                                 public sealed record ProbeQuery : IRequest<string>;

                                 [Get("/probes/{missing}")]
                                 public sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>;
                                 """;

        var secondPart = $$"""
                           using UnambitiousFx.Synapse.Endpoints;

                           namespace TestNs;

                           partial class ProbeEndpoint{{secondPartBaseList}}
                           {
                               {{secondPartBody}}
                           }
                           """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnosticsFromSources(firstPart, secondPart);

        // Assert
        Assert.Single(diagnostics, d => d.Id == "SYNE001");
    }

    [Theory]
    [InlineData(nameof(StreamTier))]
    [InlineData(nameof(MappedTier))]
    [InlineData(nameof(MappedVoidTier))]
    [InlineData(nameof(InlineTier))]
    [InlineData(nameof(InlineVoidTier))]
    public void Generate_ForEveryGeneratedTier_EmitsAPartialAndNoBinderFile(string tier)
    {
        // Arrange — every tier whose binding is generated, each satisfying its own abstract members.
        var source = tier switch
        {
            nameof(StreamTier) => StreamTier,
            nameof(MappedTier) => MappedTier,
            nameof(MappedVoidTier) => MappedVoidTier,
            nameof(InlineTier) => InlineTier,
            _ => InlineVoidTier
        };

        // Act
        var files = GeneratorHarness.GetFiles(source);

        // Assert — no type-keyed binder file survives for any generated tier.
        Assert.Contains("TestNs.ProbeEndpoint.Synapse.g.cs", files.Keys);
        Assert.DoesNotContain("SynapseEndpointBinders.g.cs", files.Keys);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }

    [Fact]
    public void Generate_ForEndpointWithRouteAttribute_EmitsCreateMetadata()
    {
        // Arrange — ProbeQuery declares Id so the {id} route parameter resolves; an unmatched route
        // parameter is SYNE001, which blocks emission entirely and would leave nothing to assert on.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ProbeQuery : IRequest<string>
                              {
                                  public string Id { get; init; } = "";
                              }

                              [Get("/probes/{id}")]
                              public sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>;
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

        // Assert — the route and verb come from the endpoint itself, not a registry.
        Assert.Contains("CreateMetadata()", generated);
        Assert.Contains("\"GET\"", generated);
        Assert.Contains("\"/probes/{id}\"", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }
}
