using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Tests.Definitions;

public sealed class TestRequestPipelineBehavior : IRequestPipelineBehavior
{
    public bool Executed { get; private set; }
    public object? RequestExecuted { get; private set; }
    public int ExecutionCount { get; private set; }
    public Action? OnExecuted { get; set; }

    public ValueTask<Result> HandleAsync<TRequest>(TRequest request,
        RequestHandlerDelegate<TRequest> next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        Executed = true;
        RequestExecuted = request;
        ExecutionCount++;
        OnExecuted?.Invoke();
        return next(request, cancellationToken);
    }

    public ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        Executed = true;
        RequestExecuted = request;
        ExecutionCount++;
        OnExecuted?.Invoke();
        return next(request, cancellationToken);
    }
}