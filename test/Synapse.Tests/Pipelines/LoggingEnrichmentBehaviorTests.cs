using System.Diagnostics;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Pipelines;

[TestSubject(typeof(LoggingEnrichmentBehavior<,>))]
public sealed class LoggingEnrichmentBehaviorTests
{
    [Fact]
    public async Task HandleAsync_WhenNextIsAsynchronous_KeepsScopeAliveUntilCompletion()
    {
        // Arrange (Given)
        var context = Substitute.For<IContext>();
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var causationId = ActivitySpanId.CreateRandom().ToHexString();
        var baggage = new Dictionary<string, string>
        {
            ["tenant.id"] = "contoso"
        };

        context.TraceId.Returns(traceId);
        context.CausationId.Returns(causationId);
        context.OccurredAt.Returns(DateTimeOffset.UnixEpoch);
        context.Baggage.Returns(baggage);

        var logger = Substitute.For<ILogger<LoggingEnrichmentBehavior<RequestWithResponseExample, int>>>();
        var scope = Substitute.For<IDisposable>();
        var disposed = false;

        scope.When(x => x.Dispose()).Do(_ => disposed = true);
        logger.BeginScope(Arg.Any<Dictionary<string, object>>()).Returns(scope);

        var behavior = new LoggingEnrichmentBehavior<RequestWithResponseExample, int>(context, logger);

        RequestHandlerDelegate<RequestWithResponseExample, int> next = async (_, _) =>
        {
            Assert.False(disposed);
            await Task.Yield();
            Assert.False(disposed);
            return Result.Success(42);
        };

        // Act (When)
        var result = await behavior.HandleAsync(new RequestWithResponseExample(), next, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.True(disposed);
        logger.Received(1).BeginScope(Arg.Is<Dictionary<string, object>>(state =>
            state != null &&
            state.ContainsKey("TraceId") &&
            state.ContainsKey("CausationId") &&
            state.ContainsKey("OccurredAt") &&
            state.ContainsKey("Baggage_tenant.id")));
    }

    [Fact]
    public async Task HandleAsync_WhenCausationIdIsNull_OmitsItFromScope()
    {
        // Arrange (Given)
        var context = Substitute.For<IContext>();
        context.TraceId.Returns(ActivityTraceId.CreateRandom().ToHexString());
        context.CausationId.Returns((string?)null);
        context.OccurredAt.Returns(DateTimeOffset.UnixEpoch);
        context.Baggage.Returns(new Dictionary<string, string>());

        var logger = Substitute.For<ILogger<LoggingEnrichmentBehavior<RequestWithResponseExample, int>>>();
        logger.BeginScope(Arg.Any<Dictionary<string, object>>()).Returns(Substitute.For<IDisposable>());

        var behavior = new LoggingEnrichmentBehavior<RequestWithResponseExample, int>(context, logger);
        RequestHandlerDelegate<RequestWithResponseExample, int> next =
            (_, _) => new ValueTask<Result<int>>(Result.Success(42));

        // Act (When)
        await behavior.HandleAsync(new RequestWithResponseExample(), next, TestContext.Current.CancellationToken);

        // Assert (Then)
        logger.Received(1).BeginScope(Arg.Is<Dictionary<string, object>>(state =>
            state != null && !state.ContainsKey("CausationId")));
    }

    [Fact]
    public async Task HandleAsync_WithContextFeatures_DoesNotLeakThemIntoScope()
    {
        // Arrange (Given) — features are process-local state such as the CQRS boundary marker, which
        // previously reached the log scope through the untyped metadata dictionary.
        var context = Substitute.For<IContext>();
        context.TraceId.Returns(ActivityTraceId.CreateRandom().ToHexString());
        context.OccurredAt.Returns(DateTimeOffset.UnixEpoch);
        context.Baggage.Returns(new Dictionary<string, string>());

        var logger = Substitute.For<ILogger<LoggingEnrichmentBehavior<RequestWithResponseExample, int>>>();
        logger.BeginScope(Arg.Any<Dictionary<string, object>>()).Returns(Substitute.For<IDisposable>());

        var behavior = new LoggingEnrichmentBehavior<RequestWithResponseExample, int>(context, logger);
        RequestHandlerDelegate<RequestWithResponseExample, int> next =
            (_, _) => new ValueTask<Result<int>>(Result.Success(42));

        // Act (When)
        await behavior.HandleAsync(new RequestWithResponseExample(), next, TestContext.Current.CancellationToken);

        // Assert (Then)
        logger.Received(1).BeginScope(Arg.Is<Dictionary<string, object>>(state =>
            state != null &&
            !state.Keys.Any(key => key.Contains("CQRS", StringComparison.Ordinal)) &&
            !state.Keys.Any(key => key.StartsWith("Metadata_", StringComparison.Ordinal))));
    }
}
