using Microsoft.Extensions.Logging;
using NSubstitute;
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
        var logger = Substitute.For<ILogger<SimpleLoggingBehavior>>();
        var behavior = new SimpleLoggingBehavior(logger);

        // Act (When)
        var result = await behavior.HandleAsync(new TestEvent(),
            () => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Event_Failure_ReturnsFailure()
    {
        // Arrange (Given)
        var logger = Substitute.For<ILogger<SimpleLoggingBehavior>>();
        var behavior = new SimpleLoggingBehavior(logger);

        // Act (When)
        var result = await behavior.HandleAsync(new TestEvent(),
            () => ValueTask.FromResult(Result.Failure("event failure")),
            CancellationToken.None);

        // Assert (Then)
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_RequestWithoutResponse_Success_ReturnsSuccess()
    {
        // Arrange (Given)
        var logger = Substitute.For<ILogger<SimpleLoggingBehavior>>();
        var behavior = new SimpleLoggingBehavior(logger);

        // Act (When)
        var result = await behavior.HandleAsync(new RequestWithoutResponse(),
            () => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_RequestWithoutResponse_Failure_ReturnsFailure()
    {
        // Arrange (Given)
        var logger = Substitute.For<ILogger<SimpleLoggingBehavior>>();
        var behavior = new SimpleLoggingBehavior(logger);

        // Act (When)
        var result = await behavior.HandleAsync(new RequestWithoutResponse(),
            () => ValueTask.FromResult(Result.Failure("request failure")),
            CancellationToken.None);

        // Assert (Then)
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_RequestWithResponse_Success_ReturnsSuccessResult()
    {
        // Arrange (Given)
        var logger = Substitute.For<ILogger<SimpleLoggingBehavior>>();
        var behavior = new SimpleLoggingBehavior(logger);

        // Act (When)
        var result = await behavior.HandleAsync<RequestWithResponse, int>(new RequestWithResponse(),
            () => ValueTask.FromResult(Result.Success(7)),
            CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_RequestWithResponse_Failure_ReturnsFailureResult()
    {
        // Arrange (Given)
        var logger = Substitute.For<ILogger<SimpleLoggingBehavior>>();
        var behavior = new SimpleLoggingBehavior(logger);

        // Act (When)
        var result = await behavior.HandleAsync<RequestWithResponse, int>(new RequestWithResponse(),
            () => ValueTask.FromResult(Result.Failure<int>("typed request failure")),
            CancellationToken.None);

        // Assert (Then)
        Assert.False(result.IsSuccess);
    }

    private sealed record TestEvent : IEvent;
    private sealed record RequestWithoutResponse : IRequest;
    private sealed record RequestWithResponse : IRequest<int>;
}
