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

        // Assert — the endpoint itself is partial; the error names the type that is not.
        var reported = Assert.Single(diagnostics, d => d.Id == "SYNE020");
        Assert.Contains("Outer", reported.GetMessage());
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
}
