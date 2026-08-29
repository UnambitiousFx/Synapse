using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Base type shared by every endpoint. Because its only abstract member is internal, endpoints
///     must derive from one of the library's own base classes rather than from this type directly.
/// </summary>
public abstract class EndpointBase
{
    /// <summary>
    ///     Builds the non-generic descriptor used to map this endpoint. Called once at startup.
    /// </summary>
    internal abstract EndpointDescriptor CreateDescriptor(EndpointMetadata metadata);

    /// <summary>
    ///     Returns request-time state that <c>CreatePlan</c> populates at startup, failing with an
    ///     explanation rather than a <see cref="NullReferenceException" /> when it is missing.
    /// </summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="state">The field holding the state.</param>
    /// <returns>The state.</returns>
    /// <exception cref="InvalidOperationException">The endpoint has not been mapped.</exception>
    /// <remarks>
    ///     The binder and the resolved configuration are created when the endpoint is mapped, so a
    ///     handler invoked before that has nothing to work with. Calling <c>HandleAsync</c> or
    ///     <c>BindAsync</c> directly — the natural way to try to unit-test an endpoint, and possible
    ///     because both are public — used to dereference a null field and produce a bare
    ///     <see cref="NullReferenceException" /> naming nothing. See docs/known-issues/056.
    /// </remarks>
    private protected TState Mapped<TState>(TState? state)
        where TState : class
    {
        return state ?? throw new InvalidOperationException(
            $"Endpoint '{GetType()}' has not been mapped, so it has no request-time state. That state " +
            "is created by MapEndpoint<TEndpoint>() (or MapSynapseEndpoints()) at startup, which means " +
            "HandleAsync and BindAsync cannot run before the endpoint is mapped. This usually means one " +
            "of them was called directly on a new instance; map the endpoint into a route builder and " +
            "exercise it through the pipeline instead.");
    }
}
