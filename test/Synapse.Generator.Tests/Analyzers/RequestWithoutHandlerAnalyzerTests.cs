using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using UnambitiousFx.Synapse.Generator.Analyzers;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

[TestSubject(typeof(RequestWithoutHandlerAnalyzer))]
public sealed class RequestWithoutHandlerAnalyzerTests
{
    private const string VoidHandler = """
        public sealed class MyHandler : IRequestHandler<MyRequest>
        {
            public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                => ValueTask.FromResult(Result.Success());
        }
        """;

    [Fact]
    public async Task Analyze_WithARequestAndNoHandler_ReportsSyn101OnTheRequest()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + "public sealed record MyRequest : IRequest;";

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN101", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("MyRequest", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithARequestAndItsHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + "public sealed record MyRequest : IRequest;\n" + VoidHandler;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAResponseRequestAndItsHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record CountQuery : IRequest<int>;

            public sealed class CountHandler : IRequestHandler<CountQuery, int>
            {
                public ValueTask<Result<int>> HandleAsync(CountQuery request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAHandlerForADifferentRequest_ReportsTheUnhandledOne()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record MyRequest : IRequest;

            public sealed record OtherRequest : IRequest;

            public sealed class OtherHandler : IRequestHandler<OtherRequest>
            {
                public ValueTask<Result> HandleAsync(OtherRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("MyRequest", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithAnAbstractRequest_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + "public abstract record BaseRequest : IRequest;";

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnOpenGenericRequest_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + "public sealed record Wrapped<T> : IRequest;";

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnOpenGenericHandler_ReportsNothing()
    {
        // Arrange (Given) — a generic handler may cover any request, so the rule cannot decide
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record MyRequest : IRequest;

            public sealed class AnyHandler<TRequest> : IRequestHandler<TRequest>
                where TRequest : IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnEventAndNoHandler_ReportsNothing()
    {
        // Arrange (Given) — zero subscribers is legal for an event
        var source = AnalyzerTestHelper.Preamble + "public sealed record PingEvent : IEvent;";

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithARequestInGeneratedCode_ReportsNothing()
    {
        // Arrange (Given)
        var source = "// <auto-generated/>\n" + AnalyzerTestHelper.Preamble +
                     "public sealed record MyRequest : IRequest;";

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAtPathAsync<RequestWithoutHandlerAnalyzer>(source, "Requests.g.cs");

        // Assert (Then)
        Assert.Empty(diagnostics);
    }
}
