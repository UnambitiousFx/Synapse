using Microsoft.Extensions.Logging.Abstractions;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse.Tests.Pipelines;

public sealed class SimpleLoggingBehaviorTests
{
    [Fact]
    public async Task HandleAsync_Event_Success_ReturnsSuccess()
    {
        // Arrange (Given)
        var behavior = new SimpleLoggingEventBehavior<TestEvent>(NullLogger<SimpleLoggingEventBehavior<TestEvent>>.Instance);

        // Act (When)
        var result = await behavior.HandleAsync(new TestEvent(),
            (_, _) => ValueTask.FromResult(Result.Success()),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Event_Failure_ReturnsFailure()
    {
        // Arrange (Given)
        var behavior = new SimpleLoggingEventBehavior<TestEvent>(NullLogger<SimpleLoggingEventBehavior<TestEvent>>.Instance);

        // Act (When)
        var result = await behavior.HandleAsync(new TestEvent(),
            (_, _) => ValueTask.FromResult(Result.Failure("event failure")),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_RequestWithoutResponse_Success_ReturnsSuccess()
    {
        // Arrange (Given)
        var behavior = new SimpleLoggingBehavior<RequestWithoutResponse>(NullLogger<SimpleLoggingBehavior<RequestWithoutResponse>>.Instance);

        // Act (When)
        var result = await behavior.HandleAsync(new RequestWithoutResponse(),
            (_, _) => ValueTask.FromResult(Result.Success()),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_RequestWithoutResponse_Failure_ReturnsFailure()
    {
        // Arrange (Given)
        var behavior = new SimpleLoggingBehavior<RequestWithoutResponse>(NullLogger<SimpleLoggingBehavior<RequestWithoutResponse>>.Instance);

        // Act (When)
        var result = await behavior.HandleAsync(new RequestWithoutResponse(),
            (_, _) => ValueTask.FromResult(Result.Failure("request failure")),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_RequestWithResponse_Success_ReturnsSuccessResult()
    {
        // Arrange (Given)
        var behavior = new SimpleLoggingBehavior<RequestWithResponse, int>(NullLogger<SimpleLoggingBehavior<RequestWithResponse, int>>.Instance);

        // Act (When)
        var result = await behavior.HandleAsync(new RequestWithResponse(),
            (_, _) => ValueTask.FromResult(Result.Success(7)),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_RequestWithResponse_Failure_ReturnsFailureResult()
    {
        // Arrange (Given)
        var behavior = new SimpleLoggingBehavior<RequestWithResponse, int>(NullLogger<SimpleLoggingBehavior<RequestWithResponse, int>>.Instance);

        // Act (When)
        var result = await behavior.HandleAsync(new RequestWithResponse(),
            (_, _) => ValueTask.FromResult(Result.Failure<int>("typed request failure")),
            TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(result.IsSuccess);
    }

    private sealed record TestEvent : IEvent;
    private sealed record RequestWithoutResponse : IRequest;
    private sealed record RequestWithResponse : IRequest<int>;
}
