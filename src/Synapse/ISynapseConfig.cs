using System.Diagnostics.CodeAnalysis;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish.Orchestrators;
using UnambitiousFx.Synapse.Publish.Outbox;

namespace UnambitiousFx.Synapse;

/// <summary>
///     Represents the configuration provider for the mediator, allowing the setup of different
///     components such as handlers, pipelines, and orchestrators.
/// </summary>
public interface ISynapseConfig
{
    private const string OpenGenericBehaviorAotMessage =
        "Open-generic pipeline behaviors require runtime code generation to close over their type arguments " +
        "and are not Native-AOT safe (value-type responses throw at resolution time). Decorate the behavior " +
        "with [PipelineBehavior] so the source generator emits closed registrations instead.";

    // ── Pipeline behaviors ───────────────────────────────────────────────────

    /// <summary>
    ///     Registers a typed request pipeline behavior for a specific request type (without response).
    ///     The behavior is resolved by DI only for requests of type <typeparamref name="TRequest" />.
    /// </summary>
    ISynapseConfig RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest>()
        where TBehavior : class, IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest;

    /// <summary>
    ///     Registers a typed request pipeline behavior for a specific request/response pair.
    ///     The behavior is resolved by DI only for requests of type <typeparamref name="TRequest" />.
    /// </summary>
    ISynapseConfig RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest, TResponse>()
        where TBehavior : class, IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull;

    /// <summary>
    ///     Registers a typed event pipeline behavior for a specific event type.
    ///     The behavior is resolved by DI only when dispatching events of type <typeparamref name="TEvent" />.
    /// </summary>
    ISynapseConfig RegisterEventPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TEvent>()
        where TBehavior : class, IEventPipelineBehavior<TEvent>
        where TEvent : class, IEvent;

    /// <summary>
    ///     Registers a typed stream pipeline behavior for a specific request/item pair.
    ///     The behavior is resolved by DI only for streaming requests of type <typeparamref name="TRequest" />.
    /// </summary>
    ISynapseConfig RegisterStreamRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest, TItem>()
        where TBehavior : class, IStreamRequestPipelineBehavior<TRequest, TItem>
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull;

    /// <summary>
    ///     Registers CQRS boundary enforcement for a specific request type (no-response form).
    /// </summary>
    /// <remarks>
    ///     Use this for handlers the source generator cannot see — handlers registered manually at runtime
    ///     (e.g. <see cref="RegisterRequestHandler{THandler,TRequest}" />) or declared in assemblies the
    ///     generator does not scan. Handlers the generator discovers are wired automatically when the assembly
    ///     carries <c>[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior&lt;&gt;))]</c>. The
    ///     registration is deduplicated and
    ///     runs outermost via <see cref="IOrderedPipelineBehavior.First" />, so calling this for a request that
    ///     the generator also covers is harmless (the behavior is wired at most once). Registration is closed
    ///     (Native-AOT safe).
    /// </remarks>
    ISynapseConfig RegisterCqrsBoundaryEnforcement<TRequest>()
        where TRequest : IRequest;

    /// <summary>
    ///     Registers CQRS boundary enforcement for a specific request/response pair.
    /// </summary>
    /// <remarks>
    ///     Use this for handlers the source generator cannot see — handlers registered manually at runtime
    ///     (e.g. <see cref="RegisterRequestHandler{THandler,TRequest,TResponse}" />) or declared in assemblies
    ///     the generator does not scan. Handlers the generator discovers are wired automatically when the
    ///     assembly carries <c>[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior&lt;,&gt;))]</c>.
    ///     The registration is deduplicated and runs outermost via <see cref="IOrderedPipelineBehavior.First" />, so calling this for
    ///     a request that the generator also covers is harmless (the behavior is wired at most once). Registration
    ///     is closed (Native-AOT safe).
    /// </remarks>
    ISynapseConfig RegisterCqrsBoundaryEnforcement<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull;

    /// <summary>
    ///     Registers an open-generic request pipeline behavior (no-response form) using the MS DI open-generic
    ///     registration mechanism. The container closes the generic type on resolution.
    ///     Use this for cross-cutting library behaviors (e.g. logging, CQRS enforcement) that apply to all request types.
    /// </summary>
    /// <param name="openGenericBehaviorType">
    ///     An open-generic type with one type parameter, e.g. <c>typeof(SimpleLoggingBehavior&lt;&gt;)</c>.
    ///     Must implement <c>IRequestPipelineBehavior&lt;TRequest&gt;</c>.
    /// </param>
    ISynapseConfig AddOpenGenericRequestPipelineBehavior(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type openGenericBehaviorType);

    /// <summary>
    ///     Registers an open-generic request pipeline behavior (with-response form) using the MS DI open-generic
    ///     registration mechanism. The container closes the generic type on resolution.
    ///     Use this for cross-cutting library behaviors (e.g. logging, CQRS enforcement) that apply to all request types.
    /// </summary>
    /// <param name="openGenericBehaviorType">
    ///     An open-generic type with two type parameters, e.g. <c>typeof(SimpleLoggingBehavior&lt;,&gt;)</c>.
    ///     Must implement <c>IRequestPipelineBehavior&lt;TRequest, TResponse&gt;</c>.
    /// </param>
    [RequiresDynamicCode(OpenGenericBehaviorAotMessage)]
    ISynapseConfig AddOpenGenericRequestWithResponsePipelineBehavior(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type openGenericBehaviorType);

    /// <summary>
    ///     Registers an open-generic event pipeline behavior using the MS DI open-generic registration mechanism.
    /// </summary>
    /// <param name="openGenericBehaviorType">
    ///     An open-generic type with one type parameter, e.g. <c>typeof(SimpleLoggingEventBehavior&lt;&gt;)</c>.
    ///     Must implement <c>IEventPipelineBehavior&lt;TEvent&gt;</c>.
    /// </param>
    ISynapseConfig AddOpenGenericEventPipelineBehavior(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type openGenericBehaviorType);

    /// <summary>
    ///     Registers an open-generic stream pipeline behavior using the MS DI open-generic registration mechanism.
    /// </summary>
    /// <param name="openGenericBehaviorType">
    ///     An open-generic type with two type parameters.
    ///     Must implement <c>IStreamRequestPipelineBehavior&lt;TRequest, TItem&gt;</c>.
    /// </param>
    [RequiresDynamicCode(OpenGenericBehaviorAotMessage)]
    ISynapseConfig AddOpenGenericStreamRequestPipelineBehavior(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type openGenericBehaviorType);

    // ── Handlers ────────────────────────────────────────────────────────────

    /// <summary>
    ///     Registers a request handler within the mediator configuration.
    /// </summary>
    ISynapseConfig RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TRequest, TResponse>()
        where TResponse : notnull
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>;

    /// <summary>
    ///     Registers a request handler (no-response) within the mediator configuration.
    /// </summary>
    ISynapseConfig RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TRequest>()
        where TRequest : IRequest
        where THandler : class, IRequestHandler<TRequest>;

    /// <summary>
    ///     Registers an event handler for a specific event type.
    /// </summary>
    ISynapseConfig RegisterEventHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TEvent>()
        where THandler : class, IEventHandler<TEvent>
        where TEvent : class, IEvent;

    /// <summary>
    ///     Registers a streaming request handler.
    /// </summary>
    ISynapseConfig RegisterStreamRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TRequest, TItem>()
        where TItem : notnull
        where TRequest : IStreamRequest<TItem>
        where THandler : class, IStreamRequestHandler<TRequest, TItem>;

    // ── Conditional handler registration ────────────────────────────────────

    /// <summary>
    ///     Registers a request handler conditionally based on a predicate evaluated at service collection build time.
    /// </summary>
    ISynapseConfig RegisterRequestHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TRequest, TResponse>(Func<bool> condition)
        where TResponse : notnull
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>;

    /// <summary>
    ///     Registers a request handler (no-response) conditionally.
    /// </summary>
    ISynapseConfig RegisterRequestHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TRequest>(Func<bool> condition)
        where TRequest : IRequest
        where THandler : class, IRequestHandler<TRequest>;

    /// <summary>
    ///     Registers an event handler conditionally.
    /// </summary>
    ISynapseConfig RegisterEventHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TEvent>(Func<bool> condition)
        where THandler : class, IEventHandler<TEvent>
        where TEvent : class, IEvent;

    // ── Infrastructure ───────────────────────────────────────────────────────

    /// <summary>
    ///     Specifies the event orchestrator implementation.
    /// </summary>
    ISynapseConfig SetEventOrchestrator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventOrchestrator>()
        where TEventOrchestrator : class, IEventOrchestrator;

    /// <summary>
    ///     Adds a register group to the mediator configuration.
    /// </summary>
    ISynapseConfig AddRegisterGroup(IRegisterGroup group);

    /// <summary>
    ///     Configures the mediator to use the specified implementation for event outbox storage.
    /// </summary>
    ISynapseConfig SetEventOutboxStorage<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventOutboxStorage>()
        where TEventOutboxStorage : class, IEventOutboxStorage;

    /// <summary>
    ///     Sets the default publishing mode for events.
    /// </summary>
    ISynapseConfig SetDefaultPublishingMode(EmitMode mode);

    /// <summary>
    ///     Configures options for the outbox retry, dead-letter and batch processing features.
    /// </summary>
    ISynapseConfig ConfigureOutbox(Action<OutboxOptions> configure);

    /// <summary>
    ///     Adds a request validator.
    /// </summary>
    ISynapseConfig AddValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TValidator, TRequest>()
        where TValidator : class, IRequestValidator<TRequest>
        where TRequest : IRequest;

    /// <summary>
    ///     Adds a request validator for requests that return a typed response.
    /// </summary>
    ISynapseConfig AddValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TValidator, TRequest, TResponse>()
        where TValidator : class, IRequestValidator<TRequest>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull;

    /// <summary>
    ///     Configures the mediator to use the default context factory implementation.
    /// </summary>
    ISynapseConfig UseDefaultContextFactory();

    /// <summary>
    ///     Configures the mediator to use the slim context factory implementation for improved performance.
    /// </summary>
    ISynapseConfig UseSlimContextFactory();

    /// <summary>
    ///     Configures the mediator to use a custom context factory implementation.
    /// </summary>
    ISynapseConfig UseContextFactory<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TContextFactory>()
        where TContextFactory : class, IContextFactory;

    /// <summary>
    ///     Applies the current configuration to set up the mediator.
    /// </summary>
    void Apply();
}
