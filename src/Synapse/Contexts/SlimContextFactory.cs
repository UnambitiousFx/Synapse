using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

internal sealed class SlimContextFactory(IEmitter emitter, IOutboxCommit outboxCommit) : IContextFactory
{
    public IContext Create()
    {
        var correlationId = Guid.NewGuid();
        return new Context(emitter, outboxCommit, correlationId);
    }
}