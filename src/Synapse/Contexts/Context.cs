using System.Diagnostics;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;

namespace UnambitiousFx.Synapse.Contexts;

internal readonly record struct Context : IContext
{
    private readonly IEmitter _emitter;
    private readonly Dictionary<Type, IContextFeature> _features;
    private readonly Dictionary<string, object> _metadata;
    private readonly IOutboxCommit _outboxCommit;

    public Context(IEmitter emitter,
        IOutboxCommit outboxCommit,
        Guid correlationId,
        IReadOnlyDictionary<Type, IContextFeature>? features = null,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        _emitter = emitter;
        _outboxCommit = outboxCommit;
        CorrelationId = correlationId;
        _metadata = metadata?.ToDictionary() ?? new Dictionary<string, object>();
        _features = features?.ToDictionary() ?? new Dictionary<Type, IContextFeature>();

        // Capture distributed tracing context from current Activity
        CaptureTracingContext();
    }


    public Context(Context context,
        IReadOnlyDictionary<Type, IContextFeature>? features = null,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        _emitter = context._emitter;
        _outboxCommit = context._outboxCommit;
        CorrelationId = context.CorrelationId;
        _metadata = metadata is not null ? Merge(metadata, context._metadata) : context._metadata;
        _features = features is not null ? Merge(features, context._features) : context._features;
    }

    public Guid CorrelationId { get; private init; }

    public void SetMetadata(string key,
        object value)
    {
        _metadata[key] = value;
    }

    public bool RemoveMetadata(string key)
    {
        return _metadata.Remove(key);
    }

    public bool TryGetMetadata<T>(string key,
        out T? value)
    {
        if (_metadata.TryGetValue(key, out var obj) &&
            obj is T tValue)
        {
            value = tValue;
            return true;
        }

        value = default;
        return false;
    }

    public T? GetMetadata<T>(string key)
    {
        if (_metadata.TryGetValue(key, out var obj) &&
            obj is T tValue)
        {
            return tValue;
        }

        return default;
    }

    public IReadOnlyDictionary<string, object> Metadata => _metadata;

    public ValueTask<Result> PublishEventAsync<TEvent>(TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        return _emitter.EmitAsync(@event, cancellationToken);
    }

    public ValueTask<Result> PublishEventAsync<TEvent>(TEvent @event,
        EmitMode mode,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        return _emitter.EmitAsync(@event, mode, cancellationToken);
    }

    public ValueTask<Result> CommitEventsAsync(CancellationToken cancellationToken = default)
    {
        return _outboxCommit.CommitAsync(cancellationToken);
    }

    public bool TryGetFeature<TFeature>(out TFeature? feature) where TFeature : class, IContextFeature
    {
        feature = GetFeature<TFeature>();
        return feature != null;
    }

    public TFeature? GetFeature<TFeature>() where TFeature : class, IContextFeature
    {
        return _features.TryGetValue(typeof(TFeature), out var value)
            ? (TFeature)value
            : null;
    }

    public TFeature MustGetFeature<TFeature>() where TFeature : class, IContextFeature
    {
        var feature = GetFeature<TFeature>();
        return feature ?? throw new MissingContextFeatureException(typeof(TFeature));
    }

    public void SetFeature<TFeature>(TFeature feature) where TFeature : class, IContextFeature
    {
        _features[typeof(TFeature)] = feature;
    }

    public void RemoveFeature<TFeature>() where TFeature : class, IContextFeature
    {
        _features.Remove(typeof(TFeature));
    }

    public IContext WithCorrelationId(Guid correlationId)
    {
        return this with { CorrelationId = correlationId };
    }

    private static Dictionary<TKey, TValue> Merge<TKey, TValue>(params IReadOnlyDictionary<TKey, TValue>[] dictionaries)
        where TKey : notnull
    {
        var merged = new Dictionary<TKey, TValue>();
        foreach (var dictionary in dictionaries)
        foreach (var kvp in dictionary)
        {
            merged[kvp.Key] = kvp.Value;
        }

        return merged;
    }

    private void CaptureTracingContext()
    {
        var activity = Activity.Current;
        if (activity == null)
        {
            return;
        }

        // Store trace and span IDs for distributed tracing correlation.
        // Guard on the actual default (all-zero) id, not on string emptiness:
        // a default ActivityTraceId/ActivitySpanId stringifies to a non-empty all-zeros string.
        if (activity.TraceId != default)
        {
            SetMetadata("Tracing.TraceId", activity.TraceId.ToString());
        }

        if (activity.SpanId != default)
        {
            SetMetadata("Tracing.SpanId", activity.SpanId.ToString());
        }

        if (activity.ParentSpanId != default)
        {
            SetMetadata("Tracing.ParentSpanId", activity.ParentSpanId.ToString());
        }

        // Store baggage for cross-service correlation
        foreach (var baggage in activity.Baggage)
        {
            if (baggage.Value != null)
            {
                SetMetadata($"Tracing.Baggage.{baggage.Key}", baggage.Value);
            }
        }
    }
}