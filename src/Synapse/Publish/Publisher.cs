using Microsoft.Extensions.Options;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish.Outbox;

namespace UnambitiousFx.Synapse.Publish;

internal sealed class Publisher : IPublisher
{
    private readonly PublishMode _defaultMode;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly IOutboxManager _outboxManager;

    public Publisher(IEventDispatcher eventDispatcher,
        IOutboxManager outboxManager,
        IOptions<PublisherOptions> options)
    {
        _eventDispatcher = eventDispatcher;
        _outboxManager = outboxManager;
        _defaultMode = options.Value.DefaultMode;
    }

    public ValueTask<Result> PublishAsync<TEvent>(TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        return PublishAsync(@event, _defaultMode, DistributionMode.Undefined, cancellationToken);
    }

    public ValueTask<Result> PublishAsync<TEvent>(TEvent @event,
        PublishMode publishMode,
        DistributionMode distributionMode,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        return publishMode switch
        {
            PublishMode.Now => _eventDispatcher.DispatchAsync(@event, distributionMode, cancellationToken),
            PublishMode.Outbox => _outboxManager.StoreAsync(@event, distributionMode, cancellationToken),
            PublishMode.Default => PublishAsync(@event, _defaultMode, distributionMode, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(publishMode), publishMode, null)
        };
    }
}