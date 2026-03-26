using Microsoft.Extensions.Options;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish.Outbox;

namespace UnambitiousFx.Synapse.Publish;

internal sealed class Emitter : IEmitter
{
    private readonly EmitMode _defaultMode;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly IOutboxManager _outboxManager;

    public Emitter(IEventDispatcher eventDispatcher,
        IOutboxManager outboxManager,
        IOptions<PublisherOptions> options)
    {
        _eventDispatcher = eventDispatcher;
        _outboxManager = outboxManager;
        _defaultMode = options.Value.DefaultMode;
    }

    public ValueTask<Result> EmitAsync<TEvent>(TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        return EmitAsync(@event, _defaultMode, cancellationToken);
    }


    public ValueTask<Result> EmitAsync<TEvent>(TEvent @event,
        EmitMode emitMode,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        // Resolve EmitMode.Default to avoid recursion
        var resolvedMode = emitMode == EmitMode.Default ? _defaultMode : emitMode;

        return resolvedMode switch
        {
            EmitMode.Now => _eventDispatcher.DispatchAsync(@event, cancellationToken),
            EmitMode.Outbox => _outboxManager.StoreAsync(@event, cancellationToken),
            EmitMode.Default => throw new InvalidOperationException(
                "EmitMode.Default cannot be used as the default publishing mode. Configure a concrete mode (Now or Outbox)."),
            _ => throw new ArgumentOutOfRangeException(nameof(emitMode), emitMode, null)
        };
    }

    public ValueTask<Result> EmitBatchAsync<TEvent>(IEnumerable<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        return EmitBatchAsync(events, _defaultMode, cancellationToken);
    }

    public async ValueTask<Result> EmitBatchAsync<TEvent>(IEnumerable<TEvent> events,
        EmitMode emitMode,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        var eventsList = events as IList<TEvent> ?? events.ToList();

        if (eventsList.Count == 0) return Result.Success();

        var resolvedMode = emitMode == EmitMode.Default ? _defaultMode : emitMode;

        // For outbox mode, batch store all events
        if (resolvedMode == EmitMode.Outbox)
        {
            var results = new List<Result>();
            foreach (var @event in eventsList)
            {
                var result = await _outboxManager.StoreAsync(@event, cancellationToken);
                results.Add(result);
            }

            // Return aggregated result
            return results.All(r => r.IsSuccess)
                ? Result.Success()
                : Result.Failure(
                    $"{results.Count(r => !r.IsSuccess)} out of {results.Count} events failed to store in outbox");
        }

        // For immediate dispatch, dispatch all events
        if (resolvedMode == EmitMode.Now)
        {
            var results = new List<Result>();
            foreach (var @event in eventsList)
            {
                var result = await _eventDispatcher.DispatchAsync(@event, cancellationToken);
                results.Add(result);
            }

            // Return aggregated result
            return results.All(r => r.IsSuccess)
                ? Result.Success()
                : Result.Failure(
                    $"{results.Count(r => !r.IsSuccess)} out of {results.Count} events failed to dispatch");
        }

        throw new InvalidOperationException(
            "EmitMode.Default cannot be used as the default publishing mode. Configure a concrete mode (Now or Outbox).");
    }
}