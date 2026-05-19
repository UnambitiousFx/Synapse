using JetBrains.Annotations;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

namespace UnambitiousFx.Synapse.Tests.Pipelines;

[TestSubject(typeof(RequestTypedBehaviorAdapter<>))]
[TestSubject(typeof(RequestTypedBehaviorAdapter<,>))]
public sealed class RequestTypedBehaviorAdapterTests
{
    #region RequestTypedBehaviorAdapter<TRequest> Tests

    [Fact]
    public async Task
        RequestTypedBehaviorAdapter_WithoutResponse_WhenRequestIsTyped_ExecutesInnerBehavior()
    {
        // Arrange (Given)
        var innerBehavior = Substitute.For<IRequestPipelineBehavior<TypedSampleRequest>>();
        var adapter = new RequestTypedBehaviorAdapter<TypedSampleRequest>(innerBehavior);
        var request = new TypedSampleRequest();
        var nextCalled = false;

        RequestHandlerDelegate<TypedSampleRequest> next = (_, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success());
        };

        innerBehavior.HandleAsync(request, Arg.Any<RequestHandlerDelegate<TypedSampleRequest>>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var del = callInfo.Arg<RequestHandlerDelegate<TypedSampleRequest>>();
                return await del(request, CancellationToken.None);
            });

        // Act (When)
        var result = await adapter.HandleAsync(request, next);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await innerBehavior.Received(1).HandleAsync(request,
            Arg.Any<RequestHandlerDelegate<TypedSampleRequest>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task
        RequestTypedBehaviorAdapter_WithoutResponse_WhenRequestIsNotTyped_SkipsInnerBehavior()
    {
        // Arrange (Given)
        var innerBehavior = Substitute.For<IRequestPipelineBehavior<TypedSampleRequest>>();
        var adapter = new RequestTypedBehaviorAdapter<TypedSampleRequest>(innerBehavior);
        var request = new DifferentRequest();
        var nextCalled = false;

        RequestHandlerDelegate<DifferentRequest> next = (_, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success());
        };

        // Act (When)
        var result = await adapter.HandleAsync(request, next);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.True(nextCalled);
        await innerBehavior.DidNotReceive().HandleAsync(Arg.Any<TypedSampleRequest>(),
            Arg.Any<RequestHandlerDelegate<TypedSampleRequest>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task
        RequestTypedBehaviorAdapter_WithoutResponse_WhenInnerBehaviorCallsNextWithIncompatibleRequest_ThrowsInvalidOperationException()
    {
        // Arrange (Given)
        var innerBehavior = Substitute.For<IRequestPipelineBehavior<TypedSampleRequest>>();
        var adapter = new RequestTypedBehaviorAdapter<TypedSampleRequest>(innerBehavior);
        var request = new TypedSampleRequest();

        RequestHandlerDelegate<TypedSampleRequest> next = (_, _) =>
            ValueTask.FromResult(Result.Success());

        // The inner behavior will call next with an incompatible request
        innerBehavior.HandleAsync(request, Arg.Any<RequestHandlerDelegate<TypedSampleRequest>>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var del = callInfo.Arg<RequestHandlerDelegate<TypedSampleRequest>>();
                // Call next with a request that's not a TypedSampleRequest
                var differentRequest = new DifferentRequest();
                // This will throw because we're calling the adapted delegate with an incompatible type
                return await del(differentRequest, CancellationToken.None);
            });

        // Act & Assert (When & Then)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await adapter.HandleAsync(request, next));

        Assert.Contains("Typed behavior for", ex.Message);
        Assert.Contains("invoked next with", ex.Message);
    }

    #endregion

    #region RequestTypedBehaviorAdapter<TRequest, TResponse> Tests

    [Fact]
    public async Task
        RequestTypedBehaviorAdapter_WithResponse_WhenRequestIsTyped_ExecutesInnerBehavior()
    {
        // Arrange (Given)
        var innerBehavior = Substitute.For<IRequestPipelineBehavior<TypedSampleRequestWithResponse, int>>();
        var adapter = new RequestTypedBehaviorAdapter<TypedSampleRequestWithResponse, int>(innerBehavior);
        var request = new TypedSampleRequestWithResponse();
        var nextCalled = false;

        RequestHandlerDelegate<TypedSampleRequestWithResponse, int> next = (_, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success(42));
        };

        innerBehavior.HandleAsync(request, Arg.Any<RequestHandlerDelegate<TypedSampleRequestWithResponse, int>>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var del = callInfo.Arg<RequestHandlerDelegate<TypedSampleRequestWithResponse, int>>();
                return await del(request, CancellationToken.None);
            });

        // Act (When)
        var result = await adapter.HandleAsync(request, next);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Match(x => x, _ => 0));
        await innerBehavior.Received(1).HandleAsync(request,
            Arg.Any<RequestHandlerDelegate<TypedSampleRequestWithResponse, int>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task
        RequestTypedBehaviorAdapter_WithResponse_WhenRequestIsNotTyped_SkipsInnerBehavior()
    {
        // Arrange (Given)
        var innerBehavior = Substitute.For<IRequestPipelineBehavior<TypedSampleRequestWithResponse, int>>();
        var adapter = new RequestTypedBehaviorAdapter<TypedSampleRequestWithResponse, int>(innerBehavior);
        var request = new DifferentRequestWithResponse();
        var nextCalled = false;

        RequestHandlerDelegate<DifferentRequestWithResponse, int> next = (_, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success(99));
        };

        // Act (When)
        var result = await adapter.HandleAsync(request, next);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.Match(x => x, _ => 0));
        Assert.True(nextCalled);
        await innerBehavior.DidNotReceive().HandleAsync(Arg.Any<TypedSampleRequestWithResponse>(),
            Arg.Any<RequestHandlerDelegate<TypedSampleRequestWithResponse, int>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task
        RequestTypedBehaviorAdapter_WithResponse_WhenInnerBehaviorCallsNextWithIncompatibleRequest_ThrowsInvalidOperationException()
    {
        // Arrange (Given)
        var innerBehavior = Substitute.For<IRequestPipelineBehavior<TypedSampleRequestWithResponse, int>>();
        var adapter = new RequestTypedBehaviorAdapter<TypedSampleRequestWithResponse, int>(innerBehavior);
        var request = new TypedSampleRequestWithResponse();

        RequestHandlerDelegate<TypedSampleRequestWithResponse, int> next = (_, _) =>
            ValueTask.FromResult(Result.Success(42));

        // The inner behavior will call next with an incompatible request
        innerBehavior.HandleAsync(request, Arg.Any<RequestHandlerDelegate<TypedSampleRequestWithResponse, int>>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var del = callInfo.Arg<RequestHandlerDelegate<TypedSampleRequestWithResponse, int>>();
                // Call next with a request that's not a TypedSampleRequestWithResponse
                var differentRequest = new DifferentRequestWithResponse();
                // This will throw because we're calling the adapted delegate with an incompatible type
                return await del(differentRequest, CancellationToken.None);
            });

        // Act & Assert (When & Then)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await adapter.HandleAsync(request, next));

        Assert.Contains("Typed behavior for", ex.Message);
        Assert.Contains("invoked next with", ex.Message);
    }

    #endregion

    #region Test Fixtures

    private sealed record TypedSampleRequest : IRequest;

    private sealed record TypedSampleRequestWithResponse : IRequest<int>;

    private sealed record DifferentRequest : IRequest;

    private sealed record DifferentRequestWithResponse : IRequest<int>;

    #endregion
}
