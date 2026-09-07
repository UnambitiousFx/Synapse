using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Tests;

/// <summary>
///     SYNE020: the rule that makes generated code mandatory rather than looked up.
/// </summary>
/// <remarks>
///     The task brief that introduced this suite named the new diagnostic SYNE019, but that ID was
///     already taken by <c>EndpointDiagnostics.InferredFormBinding</c> (shipped alongside SYNE016-018
///     for form binding) by the time this task landed. SYNE020 is used instead; nothing else about
///     the brief's design changes.
/// </remarks>
public sealed class PartialDiagnosticTests
{
    [Fact]
    public void Generate_ForNonPartialEndpoint_ReportsSyne020()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ProbeQuery : IRequest<string>;

                              [Get("/probes")]
                              public sealed class ProbeEndpoint : Endpoint<ProbeQuery, string>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        var reported = Assert.Single(diagnostics, d => d.Id == "SYNE020");
        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Contains("ProbeEndpoint", reported.GetMessage());
    }

    [Fact]
    public void Generate_ForPartialEndpointInNonPartialEnclosingType_ReportsSyne020ForTheEnclosingType()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ProbeQuery : IRequest<string>;

                              public static class Outer
                              {
                                  [Get("/probes")]
                                  public sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>;
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert — the endpoint itself is partial; the error names the type that is not, and is
        // reported on that type: anchored at the endpoint instead, the squiggle sat on a declaration
        // the author has nothing to change.
        var reported = Assert.Single(diagnostics, d => d.Id == "SYNE020");
        Assert.Contains("Outer", reported.GetMessage());
        var span = reported.Location.SourceSpan;
        Assert.Equal("Outer", source.Substring(span.Start, span.Length));
    }

    [Fact]
    public void Generate_ForPartialEndpoint_ReportsNoSyne020()
    {
        // Arrange
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ProbeQuery : IRequest<string>;

                              [Get("/probes")]
                              public sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE020");
    }

    [Fact]
    public void Generate_ForHandWrittenBindAsyncOnGeneratedTier_ReportsSyne021()
    {
        // Arrange
        const string source = """
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Binding;

                              namespace TestNs;

                              public sealed record ProbeQuery : IRequest<string>;

                              [Get("/probes")]
                              public sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>
                              {
                                  public override ValueTask<BindResult<ProbeQuery>> BindAsync(HttpContext context)
                                  {
                                      return new(BindResult<ProbeQuery>.Success(new ProbeQuery()));
                                  }
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert — without SYNE021 the only feedback is CS0111 against invisible generated code.
        var reported = Assert.Single(diagnostics, d => d.Id == "SYNE021");
        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Contains("RawEndpoint", reported.GetMessage());
    }

    [Fact]
    public void Generate_ForGeneratedTierWithNoHandWrittenBindAsync_ReportsNoSyne021()
    {
        // Arrange — the ordinary case, and the one that pins the diagnostic to a *declared* BindAsync
        // rather than to the tier. Without it, an implementation reporting SYNE021 for every endpoint
        // with a generated binder would still pass both of the other two tests.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;

                              namespace TestNs;

                              public sealed record ProbeQuery : IRequest<string>;

                              [Get("/probes")]
                              public sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>;
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE021");
    }

    [Fact]
    public void Generate_ForHandWrittenBindAsyncOnRawTier_ReportsNoSyne021()
    {
        // Arrange — RawEndpoint<...> exists precisely so BindAsync can be written by hand.
        const string source = """
                              using System.Threading.Tasks;
                              using Microsoft.AspNetCore.Http;
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Binding;

                              namespace TestNs;

                              public sealed record ProbeQuery : IRequest<string>;

                              [Get("/probes")]
                              public sealed class ProbeEndpoint : RawEndpoint<ProbeQuery, string>
                              {
                                  public override ValueTask<BindResult<ProbeQuery>> BindAsync(HttpContext context)
                                  {
                                      return new(BindResult<ProbeQuery>.Success(new ProbeQuery()));
                                  }
                              }
                              """;

        // Act
        var diagnostics = GeneratorHarness.GetDiagnostics(source);

        // Assert
        Assert.DoesNotContain(diagnostics, d => d.Id == "SYNE021");
    }

    [Fact]
    public void Generate_ForEndpointWithNoRouteAttributeAndNoConfigure_StillCompiles()
    {
        // Arrange — the "computed route" escape hatch: no attribute, route declared in Configure. The
        // generated CreateMetadata must emit empty method/route rather than omit itself, or the
        // endpoint would not satisfy the abstract member.
        const string source = """
                              using UnambitiousFx.Synapse.Abstractions;
                              using UnambitiousFx.Synapse.Endpoints;
                              using UnambitiousFx.Synapse.Endpoints.Builders;

                              namespace TestNs;

                              public sealed record ProbeQuery : IRequest<string>;

                              public sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>
                              {
                                  public override void Configure(IEndpointBuilder<string> builder)
                                  {
                                      builder.Get("/probes");
                                  }
                              }
                              """;

        // Act
        var generated = GeneratorHarness.GetEndpointFile(source);

        // Assert
        Assert.Contains("CreateMetadata()", generated);
        Assert.Contains("global::System.Array.Empty<string>()", generated);
        GeneratorHarness.AssertGeneratedCompiles(source);
    }
}
