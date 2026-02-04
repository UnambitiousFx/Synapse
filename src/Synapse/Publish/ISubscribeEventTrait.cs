using UnambitiousFx.Functional;

namespace UnambitiousFx.Synapse.Publish;

internal interface ISubscribeEventTrait
{
    Type EventType { get; }
    int MaxConcurrency { get; }
    ResultTask HandleAsync(object @event, CancellationToken cancellationToken);
}