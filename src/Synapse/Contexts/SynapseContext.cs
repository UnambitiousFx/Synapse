using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Contexts;

/// <summary>
///     The unit of work's context on the current execution branch, for components that cannot reach the DI
///     scope that owns it.
/// </summary>
/// <remarks>
///     <para>
///         <b>Prefer <see cref="IContextAccessor" /> or <see cref="IContext" />.</b> Anything resolved from the
///         scope doing the work must inject one of those; an ambient lookup there would work by accident and
///         break whenever the value is read from a sibling execution branch.
///     </para>
///     <para>
///         This exists for the components DI cannot serve: a transport's outbound hook that the library builds
///         once, at bus lifetime, with no scope handed in. <c>IHttpClientFactory</c> message handlers are one
///         (see known issue 033); a Wolverine <c>IEnvelopeRule</c> and a broker client's send interceptor are
///         others. Such a hook runs on the execution flow of the unit of work making the call even though it
///         cannot reach its scope, which is exactly what this reads.
///     </para>
///     <para>
///         Returns <c>null</c> when no context has been published on this branch. A transport hook should treat
///         that as "do not propagate" rather than creating one: inventing a flow for a call that no unit of work
///         asked for would be worse than leaving it unstamped.
///     </para>
/// </remarks>
public static class SynapseContext
{
    /// <summary>
    ///     Gets the context of the unit of work on this execution branch, or <c>null</c> when none has been
    ///     published.
    /// </summary>
    public static IContext? Current => AmbientContext.Value;
}
