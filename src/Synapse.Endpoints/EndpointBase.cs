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
    ///     The resolved configuration is created when the endpoint is mapped, so a
    ///     handler invoked before that has nothing to work with. Calling <c>HandleAsync</c> or
    ///     <c>BindAsync</c> directly — the natural way to try to unit-test an endpoint, and possible
    ///     because both are public — used to dereference a null field and produce a bare
    ///     <see cref="NullReferenceException" /> naming nothing. See docs/known-issues/056.
    ///     The harness in <c>UnambitiousFx.Synapse.Endpoints.Testing</c> exists so this is a
    ///     signpost rather than a dead end.
    /// </remarks>
    private protected TState Mapped<TState>(TState? state)
        where TState : class
    {
        return state ?? throw new InvalidOperationException(
            $"Endpoint '{GetType()}' has not been mapped, so it has no request-time state. That state " +
            "is created by MapEndpoint<TEndpoint>() (or MapSynapseEndpoints()) at startup, which means " +
            "HandleAsync and BindAsync cannot run before the endpoint is mapped. This usually means one " +
            "of them was called directly on a new instance. To exercise one endpoint on its own, use " +
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
    /// <returns>The metadata, or <see langword="null" /> while an endpoint still registers it.</returns>
    /// <remarks>
    ///     <c>protected</c> so the generated override can live in the consumer's assembly, and nullable
    ///     only transitionally: it becomes <c>abstract</c> once nothing registers metadata any more.
    /// </remarks>
    protected virtual EndpointMetadata? CreateMetadata()
    {
        return null;
    }

    /// <summary>Resolves this endpoint's metadata, preferring what its generated code declares.</summary>
    /// <param name="registered">What the registry holds, used only while the fallback exists.</param>
    /// <returns>The metadata to map with.</returns>
    internal EndpointMetadata ResolveMetadata(EndpointMetadata? registered)
    {
        // Registered metadata wins for now, deliberately. The module initializer registers every
        // endpoint, so this makes the task change no runtime behaviour: the generated CreateMetadata is
        // emitted and tested, but nothing consumes it until Task 9 deletes the registry. Preferring the
        // generated value here instead would silently discard the 148 hand-registered routes in the test
        // suite, because a generated CreateMetadata returns empty-but-non-null metadata for an endpoint
        // with no route attribute — a route the test supplied would vanish rather than fail loudly.
        return registered ?? CreateMetadata() ?? throw new InvalidOperationException(
            $"Endpoint '{GetType()}' declares no route metadata. The Synapse.Endpoints analyzer emits it " +
            "at compile time; verify it is enabled for the assembly declaring this endpoint.");
    }
}
