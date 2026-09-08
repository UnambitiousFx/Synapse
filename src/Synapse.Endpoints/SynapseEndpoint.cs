using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Base type shared by every endpoint. Because <see cref="CreateDescriptor" /> is both abstract
///     and internal, endpoints must derive from one of the library's own base classes rather than
///     from this type directly.
/// </summary>
public abstract class SynapseEndpoint
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
    ///     The resolved configuration is created when the endpoint is mapped, so a
    ///     handler invoked before that has nothing to work with. Calling <c>HandleAsync</c> directly
    ///     — the natural way to try to unit-test an endpoint, and possible because it is public —
    ///     used to dereference a null field and produce a bare
    ///     <see cref="NullReferenceException" /> naming nothing. See docs/known-issues/056.
    ///     No tier's <c>BindAsync</c> reaches here any more: binding is generated into the
    ///     endpoint's own partial and reads nothing but the request.
    ///     The harness in <c>UnambitiousFx.Synapse.Endpoints.Testing</c> exists so this is a
    ///     signpost rather than a dead end.
    /// </remarks>
    private protected TState Mapped<TState>(TState? state)
        where TState : class
    {
        return state ?? throw new InvalidOperationException(
            $"Endpoint '{GetType()}' has not been mapped, so it has no request-time state. That state " +
            "is created by MapEndpoint<TEndpoint>() (or MapSynapseEndpoints()) at startup, which means " +
            "HandleAsync cannot run before the endpoint is mapped. This usually means it was called " +
            "directly on a new instance. To exercise one endpoint on its own, use " +
            "EndpointHarness.Create<TEndpoint>() from the UnambitiousFx.Synapse.Endpoints.Testing " +
            "package, which maps it and hands back something that answers requests; otherwise map the " +
            "endpoint into a route builder and exercise it through the pipeline.");
    }

    /// <summary>
    ///     The non-body inputs this endpoint's binding reads, for OpenAPI parameter declaration, or
    ///     empty when the endpoint binds by hand.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Declared here, on the untyped base, rather than on each tier: the list is the same shape
    ///         whatever the tier binds, so one declaration serves all of them. Mirrors
    ///         <c>DeclaredRequestBody</c>. Empty is the honest answer for a hand-written
    ///         <c>BindAsync</c>: it declares no parameters.
    ///     </para>
    ///     <para>
    ///         <c>protected</c>, not <c>private protected</c>: the override is generated into the
    ///         endpoint's own partial class, which lives in the consumer's assembly, where a
    ///         <c>private protected</c> member is unreachable.
    ///     </para>
    /// </remarks>
    protected virtual IReadOnlyList<Internal.BoundParameterMetadata> DeclaredParameters()
    {
        return [];
    }

    /// <summary>
    ///     The form fields and file parts this endpoint's binding reads, or empty when it reads none.
    /// </summary>
    /// <remarks>
    ///     <c>protected</c>, not <c>private protected</c>: the override is generated into the
    ///     endpoint's own partial class, which lives in the consumer's assembly, where a
    ///     <c>private protected</c> member is unreachable.
    /// </remarks>
    protected virtual IReadOnlyList<Internal.FormFieldMetadata> DeclaredFormFields()
    {
        return [];
    }

    /// <summary>
    ///     The endpoint's route, verbs and group, generated from its attributes. Called once at startup.
    /// </summary>
    /// <returns>The metadata.</returns>
    /// <remarks>
    ///     <c>protected</c>, not <c>private protected</c>: the override is generated into the
    ///     endpoint's own partial class, which lives in the consumer's assembly, where a
    ///     <c>private protected</c> member is unreachable. <c>abstract</c> rather than a base
    ///     implementation returning empty metadata: an endpoint whose metadata was never generated must
    ///     fail to compile, not map with no route. That matters most for the hand-bound and free-form
    ///     tiers, whose routes used to come from the registry — a silent fallback would have left
    ///     <c>[Get("/health")] HealthEndpoint : BoundEndpoint</c> compiling and routeless. It is also why
    ///     every endpoint, those two tiers included, must be declared <c>partial</c>.
    /// </remarks>
    protected abstract EndpointMetadata CreateMetadata();

    /// <summary>This endpoint's route metadata, generated from its attributes.</summary>
    internal EndpointMetadata Metadata => CreateMetadata();
}
