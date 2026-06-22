using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Counter;

[RequestHandler<IncrementCounterCommand, int>]
public sealed class IncrementCounterCommandHandler : IRequestHandler<IncrementCounterCommand, int>
{
    private readonly CounterStore _store;

    public IncrementCounterCommandHandler(CounterStore store)
    {
        _store = store;
    }

    public ValueTask<Result<int>> HandleAsync(IncrementCounterCommand request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Result.Success(_store.Increment()));
}

[RequestHandler<GetCounterQuery, int>]
public sealed class GetCounterQueryHandler : IRequestHandler<GetCounterQuery, int>
{
    private readonly CounterStore _store;

    public GetCounterQueryHandler(CounterStore store)
    {
        _store = store;
    }

    public ValueTask<Result<int>> HandleAsync(GetCounterQuery request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Result.Success(_store.Current));
}

[RequestHandler<IllegalNestedCommand, int>]
public sealed class IllegalNestedCommandHandler : IRequestHandler<IllegalNestedCommand, int>
{
    private readonly IInvoker _invoker;

    public IllegalNestedCommandHandler(IInvoker invoker)
    {
        _invoker = invoker;
    }

    public async ValueTask<Result<int>> HandleAsync(IllegalNestedCommand request, CancellationToken cancellationToken = default)
    {
        // Sending another request from within a handler crosses the CQRS boundary. With enforcement enabled
        // (closed CqrsBoundaryEnforcementBehavior registrations), this throws CqrsBoundaryViolationException.
        return await _invoker.InvokeAsync(new GetCounterQuery(), cancellationToken);
    }
}
