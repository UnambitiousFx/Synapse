namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Moves flow identity and baggage across a process boundary.
/// </summary>
/// <remarks>
///     One implementation serves every transport; the transport-specific part is the
///     <see cref="IPropagationCarrier" /> handed to it.
/// </remarks>
public interface IContextPropagator
{
    /// <summary>
    ///     Writes the context's flow identity, baggage and the current trace context onto an outgoing carrier.
    /// </summary>
    /// <param name="context">The context whose state should travel.</param>
    /// <param name="carrier">The outgoing message or request.</param>
    void Inject(IContext context,
        IPropagationCarrier carrier);

    /// <summary>
    ///     Reads flow identity, baggage and trace context from an incoming carrier.
    /// </summary>
    /// <param name="carrier">The incoming message or request.</param>
    /// <returns>
    ///     The recovered state, or <see cref="PropagatedContext.None" /> when the carrier held none. Values are
    ///     validated and size-capped, so the result is safe to hand to <see cref="IContextFactory" />.
    /// </returns>
    PropagatedContext Extract(IPropagationCarrier carrier);
}
