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

    [Fact]
    public void Generate_ForTopLevelEndpoint_ReopensTheClassInItsOwnNamespace()
    {
        // Arrange — see TopLevel.

        // Act
        var generated = GeneratorHarness.GetEndpointFile(TopLevel);

        // Assert — the endpoint's own namespace, not the root namespace, and no companion type.
        Assert.Contains("namespace TestNs;", generated);
        Assert.Contains("partial class ProbeEndpoint", generated);
        Assert.DoesNotContain("IEndpointBinder", generated);
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
        // SynapseEndpointGroup.g.cs and SynapseEndpointRegistrations.g.cs are still emitted at
        // namespace scope and name every endpoint type, so a private endpoint is unreachable from
        // them (CS0122) — and an internal endpoint may not derive from Endpoint<TPrivate, string>
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
}
