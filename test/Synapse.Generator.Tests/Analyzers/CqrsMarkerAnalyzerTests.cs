using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using UnambitiousFx.Synapse.Generator.Analyzers;

namespace UnambitiousFx.Synapse.Generator.Tests.Analyzers;

/// <summary>
///     The SYN analyzers must recognize handlers written against the CQRS aliases and understand marker constraints.
/// </summary>
[TestSubject(typeof(RequestWithoutHandlerAnalyzer))]
public sealed class CqrsMarkerAnalyzerTests
{
    [Fact]
    public async Task Syn101_WithACommandHandledThroughICommandHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record CreateCommand : ICommand<int>;

            public sealed class CreateHandler : ICommandHandler<CreateCommand, int>
            {
                public ValueTask<Result<int>> HandleAsync(CreateCommand request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Syn101_WithAQueryHandledThroughIQueryHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record GetQuery : IQuery<int>;

            public sealed class GetHandler : IQueryHandler<GetQuery, int>
            {
                public ValueTask<Result<int>> HandleAsync(GetQuery request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Syn101_WithACommandAndNoHandler_ReportsSyn101()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + "public sealed record SendCommand : ICommand;";

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<RequestWithoutHandlerAnalyzer>(source);

        // Assert (Then)
        Assert.Equal("SYN101", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Syn104_WithAnUnattributedQueryHandlerNextToAnAttributedOne_ReportsSyn104()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record CreateCommand : ICommand<int>;

            public sealed record GetQuery : IQuery<int>;

            [RequestHandler<CreateCommand, int>]
            public sealed class CreateHandler : ICommandHandler<CreateCommand, int>
            {
                public ValueTask<Result<int>> HandleAsync(CreateCommand request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }

            public sealed class GetHandler : IQueryHandler<GetQuery, int>
            {
                public ValueTask<Result<int>> HandleAsync(GetQuery request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success(1));
            }
            """;

        // Act (When)
        var diagnostics = await AnalyzerTestHelper.RunAsync<UnattributedHandlerAnalyzer>(source);

        // Assert (Then)
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SYN104", diagnostic.Id);
        Assert.Contains("GetHandler", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Syn102_WithACommandOnlyBehaviorAndACommandHandler_ReportsNothing()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record SendCommand : ICommand;

            [RequestHandler<SendCommand>]
            public sealed class SendHandler : ICommandHandler<SendCommand>
            {
                public ValueTask<Result> HandleAsync(SendCommand request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class AuditBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : ICommand
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
    public async Task Syn102_WithACommandOnlyBehaviorAndOnlyAPlainRequestHandler_ReportsSyn102()
    {
        // Arrange (Given)
        var source = AnalyzerTestHelper.Preamble + """
            public sealed record PlainRequest : IRequest;

            [RequestHandler<PlainRequest>]
            public sealed class PlainHandler : IRequestHandler<PlainRequest>
            {
                public ValueTask<Result> HandleAsync(PlainRequest request, CancellationToken ct = default)
                    => ValueTask.FromResult(Result.Success());
            }

            [PipelineBehavior]
            public sealed class AuditBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
                where TRequest : ICommand
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
}
