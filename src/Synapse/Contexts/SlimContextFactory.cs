using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

internal sealed class SlimContextFactory(IPublisher publisher, IOutboxCommit outboxCommit) : IContextFactory
{
    public IContext Create()
    {
        var correlationId = Guid.NewGuid();
        return new Context(publisher, outboxCommit, correlationId);
    }
}