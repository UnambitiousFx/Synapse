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
        var correlationId = Guid.NewGuid();
        var metadata = new Dictionary<string, object>
        {
            ["Tenant"] = "contoso"
        };

        context.CorrelationId.Returns(correlationId);
        context.Metadata.Returns(metadata);

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
        var result = await behavior.HandleAsync(new RequestWithResponseExample(), next, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.True(disposed);
        logger.Received(1).BeginScope(Arg.Is<Dictionary<string, object>>(state =>
            state.ContainsKey("CorrelationId") &&
            state.ContainsKey("Metadata_Tenant")));
    }
}
