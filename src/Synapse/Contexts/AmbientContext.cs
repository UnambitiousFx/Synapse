using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

/// <summary>
///     Flows the current unit of work's context through the execution context, for consumers that cannot reach
///     the DI scope that owns it.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ContextHandler" /> owns the context; this only mirrors it, and only for components that DI
///         cannot serve. The motivating one is <see cref="Propagation.SynapsePropagationHandler" />:
///         <c>IHttpClientFactory</c> builds message handlers in a scope of its own and caches them for the
///         handler lifetime, so a scoped <see cref="IContextAccessor" /> injected into one is never the accessor
///         of the unit of work making the call.
///     </para>
///     <para>
///         Nothing else in this assembly should read this. Anything resolved from the scope doing the work
///         must inject <see cref="IContextAccessor" /> or <see cref="IContext" /> — an ambient lookup there
///         would work by accident and break whenever the value is read from a sibling execution branch.
///     </para>
///     <para>
///         <see cref="SynapseContext" /> exposes the read side publicly, for transport integrations outside
///         this assembly that face the same problem.
///     </para>
/// </remarks>
internal static class AmbientContext
{
    private static readonly AsyncLocal<IContext?> Current = new();

    /// <summary>
    ///     Gets the context of the unit of work on this execution branch, or <c>null</c> when none has been
    ///     published.
    /// </summary>
    public static IContext? Value => Current.Value;

    /// <summary>
    ///     Publishes <paramref name="context" /> to this execution branch and returns the value it replaced.
    /// </summary>
    /// <remarks>
    ///     The caller is expected to restore the previous value when the unit of work ends. Scopes nest — a
    ///     handler can dispatch through a child scope — and without a restore the outer unit of work would keep
    ///     seeing the inner context after the inner scope was gone.
    /// </remarks>
    public static IContext? Exchange(IContext? context)
    {
        var previous = Current.Value;
        Current.Value = context;
        return previous;
    }
}
