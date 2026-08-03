using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

internal sealed class InboundContextStore : IInboundContextStore
{
    public PropagatedContext Inbound { get; set; } = PropagatedContext.None;
}
