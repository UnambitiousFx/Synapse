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
        RequestHandlerDelegate<TypedSampleRequest> next,
        CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return next(request, cancellationToken);
    }
}

public sealed class
    OnlyTypedSampleRequestWithResponseBehavior : IRequestPipelineBehavior<TypedSampleRequestWithResponse, int>
{
    public int ExecutionCount { get; private set; }

    public ValueTask<Result<int>> HandleAsync(TypedSampleRequestWithResponse request,
        RequestHandlerDelegate<TypedSampleRequestWithResponse, int> next,
        CancellationToken cancellationToken = default)
    {
        ExecutionCount++;
        return next(request, cancellationToken);
    }
}
