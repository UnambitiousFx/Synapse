using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

internal sealed class DefaultContextFactory(IEmitter emitter, IOutboxCommit outboxCommit) : IContextFactory
{
    public IContext Create()
    {
#if NET9_0_OR_GREATER
        var correlationId = Guid.CreateVersion7();
#else
        var correlationId = Guid.NewGuid();
#endif
        var metadata = new Dictionary<string, object> { { "OccuredAt", DateTimeOffset.UtcNow } };
        return new Context(emitter, outboxCommit, correlationId, metadata: metadata);
    }
}