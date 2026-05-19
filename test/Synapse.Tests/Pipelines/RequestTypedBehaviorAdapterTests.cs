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

        RequestHandlerDelegate<TypedSampleRequest> next = (_, _) =>
            ValueTask.FromResult(Result.Success());

        innerBehavior.HandleAsync(request, Arg.Any<RequestHandlerDelegate<TypedSampleRequest>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var del = callInfo.Arg<RequestHandlerDelegate<TypedSampleRequest>>();
                return del(request, CancellationToken.None);
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

        RequestHandlerDelegate<DifferentRequest> next = (_, _) =>
            ValueTask.FromResult(Result.Success());

        // Act (When)
        var result = await adapter.HandleAsync(request, next);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        await innerBehavior.DidNotReceive().HandleAsync(Arg.Any<TypedSampleRequest>(),
            Arg.Any<RequestHandlerDelegate<TypedSampleRequest>>(),
            Arg.Any<CancellationToken>());
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

        RequestHandlerDelegate<TypedSampleRequestWithResponse, int> next = (_, _) =>
            ValueTask.FromResult(Result.Success(42));

        innerBehavior.HandleAsync(request, Arg.Any<RequestHandlerDelegate<TypedSampleRequestWithResponse, int>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var del = callInfo.Arg<RequestHandlerDelegate<TypedSampleRequestWithResponse, int>>();
                return del(request, CancellationToken.None);
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

        RequestHandlerDelegate<DifferentRequestWithResponse, int> next = (_, _) =>
            ValueTask.FromResult(Result.Success(99));

        // Act (When)
        var result = await adapter.HandleAsync(request, next);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.Match(x => x, _ => 0));
        await innerBehavior.DidNotReceive().HandleAsync(Arg.Any<TypedSampleRequestWithResponse>(),
            Arg.Any<RequestHandlerDelegate<TypedSampleRequestWithResponse, int>>(),
            Arg.Any<CancellationToken>());
    }



    #endregion

    #region Test Fixtures

    public sealed record TypedSampleRequest : IRequest;

    public sealed record TypedSampleRequestWithResponse : IRequest<int>;

    public sealed record DifferentRequest : IRequest;

    public sealed record DifferentRequestWithResponse : IRequest<int>;

    #endregion
}
