using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

internal sealed class DefaultContextFactory : IContextFactory
{
    public IContext Create(PropagatedContext inbound)
    {
        var context = new Context(ContextIdentity.ForUnitOfWork(inbound));
        ContextBaggage.Restore(context, inbound.Baggage);
        return context;
    }
}
