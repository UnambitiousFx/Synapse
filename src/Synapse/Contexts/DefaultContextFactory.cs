using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

internal sealed class DefaultContextFactory(IPublisher publisher, IOutboxCommit outboxCommit) : IContextFactory
{
    public IContext Create()
    {
        var correlationId = Guid.CreateVersion7();
        var metadata = new Dictionary<string, object> { { "OccuredAt", DateTimeOffset.UtcNow } };
        return new Context(publisher, outboxCommit, correlationId, metadata: metadata);
    }
}