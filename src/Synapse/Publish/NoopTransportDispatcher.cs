using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish;

internal sealed class NoopTransportDispatcher : ITransportDispatcher
{
    public ValueTask DispatchAsync<T>(T @event, CancellationToken cancellationToken = default)
        where T : class, IEvent
    {
        throw new InvalidOperationException("No transport dispatcher is registered.");
    }
}