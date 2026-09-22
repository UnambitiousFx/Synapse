using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using UnambitiousFx.Synapse.Generator.Analyzers;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

[TestSubject(typeof(BehaviorWithoutHandlersAnalyzer))]
public sealed class BehaviorWithoutHandlersAnalyzerTests
{
    private const string MyRequestAndHandler = """
        public sealed record MyRequest : IRequest;

        [RequestHandler<MyRequest>]
        public sealed class MyHandler : IRequestHandler<MyRequest>
        {
            public ValueTask<Result> HandleAsync(MyRequest request, CancellationToken ct = default)
                => ValueTask.FromResult(Result.Success());
        }

        """;

    private const string GenericBehavior = """
        [PipelineBehavior]
        public sealed class LoggingBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
            where TRequest : IRequest
        {
            public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
                CancellationToken ct = default) => next(request, ct);
        }
        """;

    private const string GlobalEntry =
        "[assembly: SynapseGlobalBehavior(typeof(LoggingBehavior<>))]\n\n";

    [Fact]
    public async Task Analyze_WithAnOpenGenericBehaviorAndAMatchingHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + MyRequestAndHandler + GenericBehavior;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAnOpenGenericBehaviorAndNoHandlerAtAll_ReportsSyn102OnTheBehavior()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + GenericBehavior;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN102", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("LoggingBehavior", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithAClosedBehaviorForARequestThatHasNoHandler_ReportsSyn102()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + MyRequestAndHandler + """
            public sealed record OtherRequest : IRequest;

            [PipelineBehavior]
            public sealed class OtherBehavior : IRequestPipelineBehavior<OtherRequest>
            {
                public ValueTask<Result> HandleAsync(OtherRequest request, RequestHandlerDelegate<OtherRequest> next,
                    CancellationToken ct = default) => next(request, ct);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN102", diagnostic.Id);
        Assert.Contains("OtherBehavior", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithAClosedBehaviorForARequestThatHasAHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + MyRequestAndHandler + """
            [PipelineBehavior]
            public sealed class MyBehavior : IRequestPipelineBehavior<MyRequest>
            {
                public ValueTask<Result> HandleAsync(MyRequest request, RequestHandlerDelegate<MyRequest> next,
                    CancellationToken ct = default) => next(request, ct);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithABehaviorWhoseConstraintNoHandlerSatisfies_ReportsSyn102()
    {
        // the only handler handles a request that is not an IAuditedRequest
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + MyRequestAndHandler + """
            public interface IAuditedRequest : IRequest;

            [PipelineBehavior]
            public sealed class AuditBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : IAuditedRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
                    CancellationToken ct = default) => next(request, ct);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Equal("SYN102", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Analyze_WithABehaviorThatHasSpecialConstraintsAndNoHandler_ReportsNothing()
    {
        // the rule cannot evaluate 'class', so it stays silent rather than guess
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            [PipelineBehavior]
            public sealed class ClassOnlyBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : class, IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
                    CancellationToken ct = default) => next(request, ct);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAGlobalBehaviorEntryAndNoHandler_ReportsSyn102OnTheAttribute()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + GlobalEntry + GenericBehavior.Replace("[PipelineBehavior]", string.Empty);

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN102", diagnostic.Id);
        Assert.Contains("LoggingBehavior", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithAGlobalBehaviorEntryAndAMatchingHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + GlobalEntry + MyRequestAndHandler +
                     GenericBehavior.Replace("[PipelineBehavior]", string.Empty);

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAHandlerInAReferencedAssembly_ReportsNothing()
    {
        // the behavior propagates to handlers of the assemblies this one references
        // Arrange (Given)
        var referenced = AnalyzerTestHelper.Preamble + MyRequestAndHandler;
        var source = AnalyzerTestHelper.Preamble + GenericBehavior;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunWithReferenceAsync<BehaviorWithoutHandlersAnalyzer>(referenced, source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithOnlyAnInternalOpenGenericHandlerInAReferencedAssembly_ReportsSyn102()
    {
        // Regression test: an *internal* type in a referenced assembly must never count as a real handler.
        // UnambitiousFx.Synapse.dll ships an internal ProxyRequestHandler<THandler, TRequest>
        // : IRequestHandler<TRequest> where TRequest : IRequest as registration plumbing. Before
        // ReferencedHandlers() filtered on DeclaredAccessibility, this shape made SYN102 silently never fire
        // for any behavior constrained to bare IRequest: the proxy's own TRequest type parameter (constrained
        // only to IRequest) satisfied MayApply's ClassifyCommonConversion check against the behavior's IRequest
        // constraint, so the behavior looked "applied" even with zero real handlers anywhere. This fixture
        // reproduces the same shape (internal generic handler over an unconstrained-but-for-IRequest type
        // parameter) in an isolated referenced assembly, so the regression is pinned independently of
        // Synapse.dll's own internals ever changing.
        // Arrange (Given)
        var referenced = AnalyzerTestHelper.Preamble + """
            internal sealed class InternalProxyHandler<THandler, TRequest> : IRequestHandler<TRequest>
                where THandler : class, IRequestHandler<TRequest>
                where TRequest : IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }
            """;
        var source = AnalyzerTestHelper.Preamble + GenericBehavior;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunWithReferenceAsync<BehaviorWithoutHandlersAnalyzer>(referenced, source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN102", diagnostic.Id);
        Assert.Contains("LoggingBehavior", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Analyze_WithAnEventBehaviorAndNoEventHandler_ReportsSyn102()
    {
        // a request handler must not satisfy an event behavior
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + MyRequestAndHandler + """
            public sealed record PingEvent : IEvent;

            [PipelineBehavior]
            public sealed class PingBehavior : IEventPipelineBehavior<PingEvent>
            {
                public ValueTask<Result> HandleAsync(PingEvent @event, EventHandlerDelegate<PingEvent> next,
                    CancellationToken ct = default) => next(@event, ct);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Equal("SYN102", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Analyze_WithAnEventBehaviorAndAnEventHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record PingEvent : IEvent;

            [EventHandler<PingEvent>]
            public sealed class PingHandler : IEventHandler<PingEvent>
            {
                public ValueTask<Result> HandleAsync(PingEvent @event, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class PingBehavior : IEventPipelineBehavior<PingEvent>
            {
                public ValueTask<Result> HandleAsync(PingEvent @event, EventHandlerDelegate<PingEvent> next,
                    CancellationToken ct = default) => next(@event, ct);
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithAPipelineBehaviorThatImplementsNoPipelineInterface_ReportsNothing()
    {
        // the generator already reports this one (MDG008)
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            [PipelineBehavior]
            public sealed class NotABehavior;
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<BehaviorWithoutHandlersAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithADualArityGlobalPairFromAReferencedAssembly_ReportsNothing()
    {
        // Arrange (Given)
        // The host only sees response handlers; the void sibling of the documented pair must not be reported
        var referenced = AnalyzerTestHelper.Preamble + """
            public sealed class LoggingBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
            {
                public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
                    RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct = default)
                    => next(request, ct);
            }

            public sealed class VoidLoggingBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : IRequest
            {
                public ValueTask<Result> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest> next,
                    CancellationToken ct = default) => next(request, ct);
            }
            """;
        var source = AnalyzerTestHelper.Preamble + """
            [assembly: SynapseGlobalBehavior(typeof(LoggingBehavior<,>))]
            [assembly: SynapseGlobalBehavior(typeof(VoidLoggingBehavior<>))]

            public sealed record CountQuery : IRequest<int>;

            [RequestHandler<CountQuery, int>]
            public sealed class CountHandler : IRequestHandler<CountQuery, int>
            {
                public ValueTask<Result<int>> HandleAsync(CountQuery request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunWithReferenceAsync<BehaviorWithoutHandlersAnalyzer>(referenced, source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyze_WithABehaviorWhoseOnlyHandlerIsInAGeneratedFile_ReportsNothing()
    {
        // Arrange (Given)
        // Generated code still counts as evidence of a handler
        var handlers = "// <auto-generated/>\n" + AnalyzerTestHelper.Preamble + MyRequestAndHandler;
        var behavior = AnalyzerTestHelper.Preamble + GenericBehavior;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunFilesAsync<BehaviorWithoutHandlersAnalyzer>(
            ("Behavior.cs", behavior), ("Handlers.g.cs", handlers));

        // Assert (Then)
        Assert.Empty(diagnostics);
    }
}
