using UnambitiousFx.Examples.MinimalApi.Counter.Messages;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Counter.Handlers;

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