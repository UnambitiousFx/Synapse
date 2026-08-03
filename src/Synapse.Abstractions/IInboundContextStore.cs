namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Holds the flow state extracted at an inbound boundary until a context is built from it.
/// </summary>
/// <remarks>
///     <para>
///         Boundary adapters write here; <see cref="IContextFactory" /> reads it when it creates the context
///         for the current scope. Because the context is built lazily on first use, an adapter can populate
///         this store at any point before the first component resolves <see cref="IContext" /> — the
///         extracted values are picked up regardless of ordering.
///     </para>
///     <para>
///         This exists so that adopting inbound identity never means mutating an already-created context.
///         Mutation would leave earlier readers holding a context with a different correlation id.
///     </para>
/// </remarks>
public interface IInboundContextStore
{
    /// <summary>
    ///     Gets or sets the flow state extracted from the inbound boundary.
    ///     Defaults to <see cref="PropagatedContext.None" />.
    /// </summary>
    PropagatedContext Inbound { get; set; }
}
