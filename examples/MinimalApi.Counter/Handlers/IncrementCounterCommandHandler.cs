using UnambitiousFx.Examples.MinimalApi.Counter.Messages;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Counter.Handlers;

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