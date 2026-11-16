using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.AuthorizationAot;

// Gives the describer an event to describe by Type under NativeAOT.
public sealed record Pinged : IEvent;

[EventHandler<Pinged>]
public sealed class PingedHandler : IEventHandler<Pinged>
{
    public ValueTask<Result> HandleAsync(Pinged @event, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Result.Success());
}
