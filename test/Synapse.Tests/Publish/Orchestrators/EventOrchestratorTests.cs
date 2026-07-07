using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish.Orchestrators;

namespace UnambitiousFx.Synapse.Tests.Publish.Orchestrators;

public sealed class EventOrchestratorTests
{
    [Fact]
    public async Task SequentialOrchestrator_WhenAllHandlersSucceed_ReturnsSuccess()
    {
        // Arrange (Given)
        var @event = new TestEvent("e1");
        var handler1 = new ConstantResultHandler(Result.Success());
        var handler2 = new ConstantResultHandler(Result.Success());

        var orchestrator = new SequentialEventOrchestrator();

        // Act (When)
        var result = await orchestrator.RunAsync(new[] { handler1, handler2 }, @event, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(1, handler1.CallCount);
        Assert.Equal(1, handler2.CallCount);
    }

    [Fact]
    public async Task SequentialOrchestrator_WhenOneHandlerFails_ReturnsFailure()
    {
        // Arrange (Given)
        var @event = new TestEvent("e2");
        var handler1 = new ConstantResultHandler(Result.Success());
        var handler2 = new ConstantResultHandler(Result.Failure("boom"));

        var orchestrator = new SequentialEventOrchestrator();

        // Act (When)
        var result = await orchestrator.RunAsync(new[] { handler1, handler2 }, @event, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ConcurrentOrchestrator_WhenHandlersRun_ReturnsCombinedResult()
    {
        // Arrange (Given)
        var @event = new TestEvent("e3");
        var handler1 = new ConstantResultHandler(Result.Success());
        var handler2 = new ConstantResultHandler(Result.Failure("concurrent failure"));

        var orchestrator = new ConcurrentEventOrchestrator();

        // Act (When)
        var result = await orchestrator.RunAsync(new[] { handler1, handler2 }, @event, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(result.IsSuccess);
    }

    private sealed record TestEvent(string Name) : IEvent;

    private sealed class ConstantResultHandler(Result result) : IEventHandler<TestEvent>
    {
        public int CallCount { get; private set; }

        public ValueTask<Result> HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(result);
        }
    }
}
