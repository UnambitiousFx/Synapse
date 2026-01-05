using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

internal sealed class ContextBuilder : IContextBuilder
{
    private readonly Dictionary<string, object> _metadata;
    private readonly IOutboxCommit _outboxCommit;
    private readonly IPublisher _publisher;
    private Guid? _correlationId;

    public ContextBuilder(IPublisher publisher, IOutboxCommit outboxCommit)
    {
        _publisher = publisher;
        _outboxCommit = outboxCommit;
        _metadata = new Dictionary<string, object>();
    }

    public IContextBuilder WithCorrelationId(Guid correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public IContextBuilder WithMetadata(Dictionary<string, object> metadata)
    {
        foreach (var kv in metadata)
            _metadata[kv.Key] = kv.Value;
        return this;
    }

    public IContextBuilder WithMetadata(string key, object value)
    {
        _metadata[key] = value;
        return this;
    }

    public IContext Build()
    {
        ArgumentNullException.ThrowIfNull(_correlationId);
        return new Context(_publisher, _outboxCommit, _correlationId.Value, null, _metadata);
    }
}