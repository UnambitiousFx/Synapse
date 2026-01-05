using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Publish;

internal interface ISubscribeEventTrait
{
    Type EventType { get; }
    int MaxConcurrency { get; }
    ValueTask<Result> HandleAsync(object @event, CancellationToken cancellationToken);
}