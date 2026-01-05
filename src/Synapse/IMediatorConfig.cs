using System.Diagnostics.CodeAnalysis;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish.Orchestrators;
using UnambitiousFx.Synapse.Publish.Outbox;

namespace UnambitiousFx.Synapse;

/// <summary>
///     Represents the configuration provider for the mediator, allowing the setup of different
///     components such as handlers, pipelines, and orchestrators.
/// </summary>
public interface IMediatorConfig
{
    /// <summary>
    ///     Registers a request pipeline behavior to be included in the mediator's processing pipeline.
    /// </summary>
    /// <typeparam name="TRequestPipelineBehavior">
    ///     The type of the request pipeline behavior that implements <see cref="IRequestPipelineBehavior" />.
    ///     This type must have a public constructor to be resolved at runtime.
    /// </typeparam>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, enabling further configuration chaining.
    /// </returns>
    IMediatorConfig RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TRequestPipelineBehavior>()
        where TRequestPipelineBehavior : class, IRequestPipelineBehavior;

    /// <summary>
    ///     Registers a request pipeline behavior to be included in the mediator's processing pipeline.
    ///     This overload is for typed registrations that do not have a response.
    /// </summary>
    /// <typeparam name="TBehavior">
    ///     The type of the behavior that implements <see cref="IRequestPipelineBehavior{TRequest}" />.
    ///     This type must have a public constructor to be resolved at runtime.
    /// </typeparam>
    /// <typeparam name="TRequest">
    ///     The type of the request that the behavior processes. Must implement <see cref="IRequest" />.
    /// </typeparam>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, enabling further configuration chaining.
    /// </returns>
    IMediatorConfig RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest>()
        where TBehavior : class, IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest;

    /// <summary>
    ///     Registers a request pipeline behavior to be included in the mediator's processing pipeline.
    ///     This overload is for typed registrations that have a response.
    /// </summary>
    /// <typeparam name="TBehavior">
    ///     The type of the behavior that implements <see cref="IRequestPipelineBehavior{TRequest, TResponse}" />.
    ///     This type must have a public constructor to be resolved at runtime.
    /// </typeparam>
    /// <typeparam name="TRequest">
    ///     The type of the request that the behavior processes. Must implement <see cref="IRequest{TResponse}" />.
    /// </typeparam>
    /// <typeparam name="TResponse">
    ///     The type of the response that the behavior generates. Must be a non-nullable type.
    /// </typeparam>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, enabling further configuration chaining.
    /// </returns>
    IMediatorConfig RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest,
        TResponse>()
        where TBehavior : class, IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull;

    /// <summary>
    ///     Registers a request pipeline behavior conditionally, based on a predicate.
    ///     The behavior is registered only if the predicate evaluates to true.
    /// </summary>
    /// <typeparam name="TBehavior">
    ///     The type of the behavior that implements <see cref="IRequestPipelineBehavior" />.
    ///     This type must have a public constructor to be resolved at runtime.
    /// </typeparam>
    /// <param name="predicate">
    ///     A function that determines whether the behavior should be applied, based on the context and request.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, enabling further configuration chaining.
    /// </returns>
    IMediatorConfig RegisterConditionalRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior>(
        Func<object, bool> predicate)
        where TBehavior : class, IRequestPipelineBehavior;

    /// <summary>
    ///     Registers a request pipeline behavior conditionally, based on a predicate.
    ///     This overload is for typed registrations that do not have a response.
    /// </summary>
    /// <typeparam name="TBehavior">
    ///     The type of the behavior that implements <see cref="IRequestPipelineBehavior{TRequest}" />.
    ///     This type must have a public constructor to be resolved at runtime.
    /// </typeparam>
    /// <typeparam name="TRequest">
    ///     The type of the request that the behavior processes. Must implement <see cref="IRequest" />.
    /// </typeparam>
    /// <param name="predicate">
    ///     A function that determines whether the behavior should be applied, based on the context and request.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, enabling further configuration chaining.
    /// </returns>
    IMediatorConfig RegisterConditionalRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest>(
        Func<TRequest, bool> predicate)
        where TBehavior : class, IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest;

    /// <summary>
    ///     Registers a request pipeline behavior conditionally, based on a predicate.
    ///     This overload is for typed registrations that have a response.
    /// </summary>
    /// <typeparam name="TBehavior">
    ///     The type of the behavior that implements <see cref="IRequestPipelineBehavior{TRequest, TResponse}" />.
    ///     This type must have a public constructor to be resolved at runtime.
    /// </typeparam>
    /// <typeparam name="TRequest">
    ///     The type of the request that the behavior processes. Must implement <see cref="IRequest{TResponse}" />.
    /// </typeparam>
    /// <typeparam name="TResponse">
    ///     The type of the response that the behavior generates. Must be a non-nullable type.
    /// </typeparam>
    /// <param name="predicate">
    ///     A function that determines whether the behavior should be applied, based on the context and request.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, enabling further configuration chaining.
    /// </returns>
    IMediatorConfig RegisterConditionalRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest, TResponse>(
        Func<TRequest, bool> predicate)
        where TBehavior : class, IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull;

    /// <summary>
    ///     Registers a custom event pipeline behavior to be included in the mediator configuration.
    /// </summary>
    /// <typeparam name="TEventPipelineBehavior">
    ///     The type of the event pipeline behavior to register. The type must implement <see cref="IEventPipelineBehavior" />
    ///     and
    ///     must have a public parameterless constructor.
    /// </typeparam>
    /// <returns>
    ///     The instance of <see cref="IMediatorConfig" />, enabling method chaining for further configuration.
    /// </returns>
    IMediatorConfig RegisterEventPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventPipelineBehavior>()
        where TEventPipelineBehavior : class, IEventPipelineBehavior;

    /// <summary>
    ///     Specifies the event orchestrator implementation to be used for coordinating the execution
    ///     of event handlers. The specified type must implement the <see cref="IEventOrchestrator" /> interface.
    /// </summary>
    /// <typeparam name="TEventOrchestrator">
    ///     The type of the event orchestrator to be registered. Must have accessible public constructors
    ///     and implement <see cref="IEventOrchestrator" />.
    /// </typeparam>
    /// <returns>
    ///     The current <see cref="IMediatorConfig" /> instance to allow method chaining.
    /// </returns>
    IMediatorConfig SetEventOrchestrator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventOrchestrator>()
        where TEventOrchestrator : class, IEventOrchestrator;

    /// <summary>
    ///     Adds a register group to the mediator configuration.
    /// </summary>
    /// <param name="group">
    ///     The instance of <see cref="IRegisterGroup" /> that contains the registrations to be added.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, enabling fluent configuration.
    /// </returns>
    IMediatorConfig AddRegisterGroup(IRegisterGroup group);

    /// <summary>
    ///     Registers a request handler within the mediator configuration to handle requests of a specific type and produce
    ///     responses of a specific type.
    /// </summary>
    /// <typeparam name="THandler">
    ///     The type of the handler that processes the request. Must implement
    ///     <see cref="IRequestHandler{TRequest, TResponse}" />.
    /// </typeparam>
    /// <typeparam name="TRequest">
    ///     The type of the request that the handler processes. Must implement <see cref="IRequest{TResponse}" />.
    /// </typeparam>
    /// <typeparam name="TResponse">
    ///     The type of the response that the handler generates. Must be a non-nullable type.
    /// </typeparam>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, enabling method chaining for additional configuration.
    /// </returns>
    IMediatorConfig RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TRequest, TResponse>()
        where TResponse : notnull
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>;

    /// <summary>
    ///     Registers a request handler and its associated request type with the mediator's configuration.
    /// </summary>
    /// <typeparam name="THandler">
    ///     The type of the request handler to be registered. Must implement <see cref="IRequestHandler{TRequest}" />.
    /// </typeparam>
    /// <typeparam name="TRequest">
    ///     The type of the request that the handler processes. Must implement <see cref="IRequest" />.
    /// </typeparam>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, enabling chained configuration.
    /// </returns>
    IMediatorConfig RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TRequest>()
        where TRequest : IRequest
        where THandler : class, IRequestHandler<TRequest>;

    /// <summary>
    ///     Registers an event handler for a specific event type.
    /// </summary>
    /// <typeparam name="THandler">
    ///     The handler type that implements <see cref="IEventHandler{TEvent}" />.
    /// </typeparam>
    /// <typeparam name="TEvent">
    ///     The event type that the handler will process, implementing <see cref="IEvent" />.
    /// </typeparam>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, allowing for fluent configuration.
    /// </returns>
    IMediatorConfig RegisterEventHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TEvent>()
        where THandler : class, IEventHandler<TEvent>
        where TEvent : class, IEvent;

    /// <summary>
    ///     Configures the mediator to use the specified implementation for event outbox storage.
    ///     The event outbox storage is responsible for persisting events and tracking their delivery status.
    /// </summary>
    /// <typeparam name="TEventOutboxStorage">
    ///     The type of the event outbox storage implementation. Must implement <see cref="IEventOutboxStorage" />.
    /// </typeparam>
    /// <returns>
    ///     The current <see cref="IMediatorConfig" /> instance, allowing for method chaining.
    /// </returns>
    IMediatorConfig SetEventOutboxStorage<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventOutboxStorage>()
        where TEventOutboxStorage : class, IEventOutboxStorage;

    /// <summary>
    ///     Sets the default publishing mode for events in the mediator configuration.
    /// </summary>
    /// <param name="mode">The <see cref="PublishMode" /> to set as the default publishing mode.</param>
    /// <returns>An instance of <see cref="IMediatorConfig" /> to allow for method chaining.</returns>
    IMediatorConfig SetDefaultPublishingMode(PublishMode mode);

    /// <summary>
    ///     Configures options for the outbox retry, dead-letter and batch processing features.
    /// </summary>
    /// <param name="configure">Delegate to mutate the <see cref="OutboxOptions" /> instance.</param>
    /// <returns>The current config instance.</returns>
    IMediatorConfig ConfigureOutbox(Action<OutboxOptions> configure);

    /// <summary>
    ///     Enables CQRS boundary enforcement to prevent violations such as:
    ///     - Commands being sent within command handlers
    ///     - Queries being sent within query handlers
    ///     - Commands being sent within query handlers
    /// </summary>
    /// <param name="enable">True to enable CQRS boundary enforcement, false to disable it. Default is true.</param>
    /// <returns>The current config instance.</returns>
    IMediatorConfig EnableCqrsBoundaryEnforcement(bool enable = true);

    /// <summary>
    ///     Adds a request validator to the mediator configuration.
    /// </summary>
    /// <typeparam name="TValidator">
    ///     The type of the validator, implementing <see cref="IRequestValidator{TRequest}" />.
    /// </typeparam>
    /// <typeparam name="TRequest">
    ///     The type of the request that the validator applies to, implementing <see cref="IRequest" />.
    /// </typeparam>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, allowing for fluent configuration.
    /// </returns>
    IMediatorConfig AddValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TValidator, TRequest>()
        where TValidator : class, IRequestValidator<TRequest>
        where TRequest : IRequest;

    /// <summary>
    ///     Configures the mediator to use the default context factory implementation.
    /// </summary>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, allowing for fluent configuration.
    /// </returns>
    IMediatorConfig UseDefaultContextFactory();

    /// <summary>
    ///     Configures the mediator to use the slim context factory implementation for improved performance.
    /// </summary>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, allowing for fluent configuration.
    /// </returns>
    IMediatorConfig UseSlimContextFactory();

    /// <summary>
    ///     Configures the mediator to use a custom context factory implementation.
    /// </summary>
    /// <typeparam name="TContextFactory">
    ///     The type of the context factory to use. Must implement <see cref="IContextFactory" />.
    /// </typeparam>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, allowing for fluent configuration.
    /// </returns>
    IMediatorConfig UseContextFactory<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TContextFactory>()
        where TContextFactory : class, IContextFactory;

    /// <summary>
    ///     Registers an event routing filter that determines how events should be distributed.
    ///     Filters are evaluated in order of their <see cref="IEventRoutingFilter.Order" /> property.
    /// </summary>
    /// <typeparam name="TEventRoutingFilter">
    ///     The type of the routing filter to register. Must implement <see cref="IEventRoutingFilter" />.
    /// </typeparam>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, enabling method chaining.
    /// </returns>
    /// <remarks>
    ///     Multiple routing filters can be registered. They will be evaluated in ascending order
    ///     of their Order property. The first filter that returns a non-null distribution mode
    ///     determines how the event is routed.
    /// </remarks>
    IMediatorConfig RegisterEventRoutingFilter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventRoutingFilter>()
        where TEventRoutingFilter : class, IEventRoutingFilter;

    /// <summary>
    ///     Configures the mediator to enable distributed event functionality using the specified transport dispatcher.
    /// </summary>
    /// <param name="defineTransport">
    ///     A function that defines the transport configuration by providing an instance of
    ///     <see cref="IDistributedEventConfig" /> and returning an instance of <see cref="ITransportConfig" />.
    /// </param>
    /// <param name="configureTransport">
    ///     An action that configures the transport using an instance of <see cref="ITransportConfig" />.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, allowing for fluent configuration.
    /// </returns>
    IMediatorConfig EnableDistributedEvent(Func<IDistributedEventConfig, ITransportConfig> defineTransport,
        Action<ITransportConfig> configureTransport);

    /// <summary>
    ///     Registers event dispatcher delegates using a generated registration class.
    ///     This is typically used with source-generated IEventDispatcherRegistration implementations
    ///     for NativeAOT compatibility.
    /// </summary>
    /// <typeparam name="TRegistration">
    ///     The type of the registration class. Must implement <see cref="IEventDispatcherRegistration" />.
    /// </typeparam>
    /// <returns>
    ///     The current instance of <see cref="IMediatorConfig" />, enabling method chaining.
    /// </returns>
    /// <remarks>
    ///     This method should be called with the generated EventDispatcherRegistration class
    ///     to register typed dispatcher delegates for all event types, avoiding reflection
    ///     during outbox replay.
    /// </remarks>
    IMediatorConfig UseEventDispatcherRegistration<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TRegistration>()
        where TRegistration : class, IEventDispatcherRegistration, new();

    /// <summary>
    ///     Applies the current configuration to set up the mediator with the provided services and options.
    ///     This method finalizes the configuration by ensuring that all registered components such as
    ///     handlers, orchestrators, and storage are added to the service collection. The configurations
    ///     for publishing and event dispatching are also initialized during this process.
    /// </summary>
    void Apply();
}