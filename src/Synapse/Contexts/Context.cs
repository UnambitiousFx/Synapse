using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;

namespace UnambitiousFx.Synapse.Contexts;

internal readonly record struct Context : IContext
{
    private readonly Dictionary<Type, IContextFeature> _features;
    private readonly Dictionary<string, object> _metadata;
    private readonly IOutboxCommit _outboxCommit;
    private readonly IPublisher _publisher;

    public Context(IPublisher publisher,
        IOutboxCommit outboxCommit,
        Guid correlationId,
        IReadOnlyDictionary<Type, IContextFeature>? features = null,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        _publisher = publisher;
        _outboxCommit = outboxCommit;
        CorrelationId = correlationId;
        _metadata = metadata?.ToDictionary() ?? new Dictionary<string, object>();
        _features = features?.ToDictionary() ?? new Dictionary<Type, IContextFeature>();
    }


    public Context(Context context,
        IReadOnlyDictionary<Type, IContextFeature>? features = null,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        _publisher = context._publisher;
        _outboxCommit = context._outboxCommit;
        CorrelationId = context.CorrelationId;
        _metadata = metadata is not null ? Merge(metadata, context._metadata) : context._metadata;
        _features = features is not null ? Merge(features, context._features) : context._features;
    }

    public Guid CorrelationId { get; }

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
            return tValue;

        return default;
    }

    public IReadOnlyDictionary<string, object> Metadata => _metadata;

    public ValueTask<Result> PublishEventAsync<TEvent>(TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        return _publisher.PublishAsync(@event, cancellationToken);
    }

    public ValueTask<Result> PublishEventAsync<TEvent>(TEvent @event,
        PublishMode mode,
        DistributionMode distributionMode,
        CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        return _publisher.PublishAsync(@event, mode, distributionMode, cancellationToken);
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

    private static Dictionary<TKey, TValue> Merge<TKey, TValue>(params IReadOnlyDictionary<TKey, TValue>[] dictionaries)
        where TKey : notnull
    {
        var merged = new Dictionary<TKey, TValue>();
        foreach (var dictionary in dictionaries)
        foreach (var kvp in dictionary)
            merged[kvp.Key] = kvp.Value;

        return merged;
    }
}