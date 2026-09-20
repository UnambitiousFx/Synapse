using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using UnambitiousFx.Synapse.Generator.Analyzers;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

[TestSubject(typeof(HandlerAttributeOnRecordAnalyzer))]
public sealed class HandlerAttributeOnRecordAnalyzerTests
{
    private const string Request = "public sealed record MyRequest : IRequest;\n";

    [Fact]
    public async Task Analyze_WithRequestHandlerAttributeOnARecord_ReportsSyn103OnTheAttribute()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + Request + """
            [RequestHandler<MyRequest>]
            public sealed record MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<HandlerAttributeOnRecordAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN103", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("RequestHandler<MyRequest>", source.Substring(diagnostic.Location.SourceSpan.Start,
            diagnostic.Location.SourceSpan.Length));
    }

    [Fact]
    public async Task Analyze_WithEventHandlerAttributeOnARecord_ReportsSyn103()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record PingEvent : IEvent;

            [EventHandler<PingEvent>]
            public sealed record PingHandler : IEventHandler<PingEvent>
            {
                public ValueTask<Result> HandleAsync(PingEvent @event, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<HandlerAttributeOnRecordAnalyzer>(source);

        // Assert (Then)
        Assert.Equal("SYN103", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Analyze_WithHandlerAttributeOnAClass_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + Request + """
            [RequestHandler<MyRequest>]
            public sealed class MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<HandlerAttributeOnRecordAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithARecordThatHasNoHandlerAttribute_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + Request + """
            public sealed record MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<HandlerAttributeOnRecordAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnUnrelatedAttributeOnARecord_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + Request + """
            [System.Obsolete]
            public sealed record MyHandler : IRequestHandler<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<HandlerAttributeOnRecordAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithoutAReferenceToSynapse_ReportsNothing()
    {
        // Arrange (Given)
        const string source = """
            [System.Obsolete]
            public sealed record Plain;
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<HandlerAttributeOnRecordAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }
}
