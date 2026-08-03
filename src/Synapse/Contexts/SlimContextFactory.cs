using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

/// <summary>
///     Creates a context that carries flow identity but <b>not</b> baggage.
/// </summary>
/// <remarks>
///     Identity is derived from W3C trace context exactly as in <see cref="DefaultContextFactory" /> — there is
///     no cheaper trace id to generate — so the only difference is that inbound baggage is discarded rather than
///     restored onto the context. Choose this when the application does not propagate business values and does
///     not want to pay for parsing and holding them; it must be selected explicitly, so dropping baggage is a
///     stated decision rather than a surprise.
/// </remarks>
internal sealed class SlimContextFactory : IContextFactory
{
    public IContext Create(PropagatedContext inbound)
    {
        return new Context(ContextIdentity.ForUnitOfWork(inbound));
    }
}
