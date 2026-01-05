using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Definitions;

public sealed record TypedSampleRequest : IRequest;

public sealed record TypedSampleRequestWithResponse(int Value) : IRequest<int>;

public interface IBaseRequest : IRequest;

public abstract record BaseRequest : IBaseRequest;

public sealed record TypedSampleInheritanceRequest : BaseRequest, IRequest;

public sealed class OnlyTypedSampleRequestBehavior : IRequestPipelineBehavior<TypedSampleRequest>
{
    public int ExecutionCount { get; private set; }

    public ValueTask<Result> HandleAsync(TypedSampleRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return next();
    }
}

public sealed class
    OnlyTypedSampleRequestWithResponseBehavior : IRequestPipelineBehavior<TypedSampleRequestWithResponse, int>
{
    public int ExecutionCount { get; private set; }

    public ValueTask<Result<int>> HandleAsync(TypedSampleRequestWithResponse request,
        RequestHandlerDelegate<int> next,
        CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return next();
    }
}

// Unconditional inner behavior; conditional execution is controlled by wrapper registration predicate
public sealed class ConditionalTypedRequestBehavior : IRequestPipelineBehavior<TypedSampleRequest>
{
    public int ExecutionCount { get; private set; }

    public ValueTask<Result> HandleAsync(TypedSampleRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return next();
    }
}

public sealed class InterfaceTypedRequestBehavior : IRequestPipelineBehavior<IBaseRequest>
{
    public int ExecutionCount { get; private set; }

    public ValueTask<Result> HandleAsync(IBaseRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return next();
    }
}

public sealed class AbstractTypedRequestBehavior : IRequestPipelineBehavior<BaseRequest>
{
    public int ExecutionCount { get; private set; }

    public ValueTask<Result> HandleAsync(BaseRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return next();
    }
}