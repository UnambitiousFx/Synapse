using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Definitions;

/// <summary>Generic test behavior for requests without a response.</summary>
public sealed class TestRequestPipelineBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    public bool Executed { get; private set; }
    public object? RequestExecuted { get; private set; }
    public int ExecutionCount { get; private set; }
    public Action? OnExecuted { get; set; }

    public ValueTask<Result> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest> next,
        CancellationToken cancellationToken = default)
    {
        Executed = true;
        RequestExecuted = request;
        ExecutionCount++;
        OnExecuted?.Invoke();
        return next(request, cancellationToken);
    }
}

/// <summary>Generic test behavior for requests with a response.</summary>
public sealed class TestRequestPipelineBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    public bool Executed { get; private set; }
    public object? RequestExecuted { get; private set; }
    public int ExecutionCount { get; private set; }
    public Action? OnExecuted { get; set; }

    public ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
    {
        Executed = true;
        RequestExecuted = request;
        ExecutionCount++;
        OnExecuted?.Invoke();
        return next(request, cancellationToken);
    }
}
