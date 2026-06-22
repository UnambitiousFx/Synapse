using System.Diagnostics.CodeAnalysis;

namespace UnambitiousFx.Synapse.Abstractions;

/// Provides a contract for configuring and registering dependencies related to request and event handling within a dependency injection container.
public interface IDependencyInjectionBuilder
{
    /// <summary>
    ///     Registers a request handler implementation for a specific request and response type with the dependency injection
    ///     system.
    /// </summary>
    /// <typeparam name="TRequestHandler">
    ///     The type of the request handler to be registered, which must implement
    ///     <see cref="IRequestHandler{TRequest, TResponse}" />.
    /// </typeparam>
    /// <typeparam name="TRequest">
    ///     The type of the request being handled, which must implement <see cref="IRequest{TResponse}" />.
    /// </typeparam>
    /// <typeparam name="TResponse">
    ///     The type of the response returned by the handler, ensuring it is not null.
    /// </typeparam>
    /// <remarks>
    ///     This method is typically used to register a request handler that processes a specific request and returns a typed
    ///     response.
    ///     It enables decoupling of the request processing logic and promotes testability and maintainability.
    /// </remarks>
    IDependencyInjectionBuilder RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TRequestHandler, TRequest,
        TResponse>()
        where TRequestHandler : class, IRequestHandler<TRequest, TResponse>
        where TResponse : notnull
        where TRequest : IRequest<TResponse>;

    /// Registers a request handler for handling a specified request type without producing a response.
    /// TRequestHandler: The type of the request handler to be registered. Must implement the IRequestHandler interface with the specified TRequest type.
    /// TRequest: The type of the request to be handled. Must implement the IRequest interface.
    /// This method is used to register a request handler that processes a specific request type without returning a response.
    /// The method ensures that the appropriate request handler is associated with its corresponding request type for processing.
    IDependencyInjectionBuilder RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TRequestHandler, TRequest>()
        where TRequestHandler : class, IRequestHandler<TRequest>
        where TRequest : IRequest;

    /// <summary>
    ///     Registers an event handler for a specific event type in the dependency injection system.
    /// </summary>
    /// <typeparam name="TEventHandler">
    ///     The type of the event handler to be registered. Must implement the <see cref="IEventHandler{TEvent}" /> interface
    ///     and have public constructors.
    /// </typeparam>
    /// <typeparam name="TEvent">
    ///     The type of the event that the event handler processes. Must implement the <see cref="IEvent" /> interface.
    /// </typeparam>
    /// <remarks>
    ///     This method is typically used to bind event types to their corresponding handlers within the dependency injection
    ///     container,
    ///     enabling automatic resolution and invocation of the handlers during event processing.
    /// </remarks>
    IDependencyInjectionBuilder RegisterEventHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventHandler, TEvent>()
        where TEventHandler : class, IEventHandler<TEvent>
        where TEvent : class, IEvent;

    /// <summary>
    ///     Registers a streaming request handler implementation for a specific streaming request and item type with the
    ///     dependency injection system.
    /// </summary>
    /// <typeparam name="TStreamRequestHandler">
    ///     The type of the streaming request handler to be registered, which must implement
    ///     <see cref="IStreamRequestHandler{TRequest, TItem}" />.
    /// </typeparam>
    /// <typeparam name="TRequest">
    ///     The type of the streaming request being handled, which must implement <see cref="IStreamRequest{TItem}" />.
    /// </typeparam>
    /// <typeparam name="TItem">
    ///     The type of items yielded by the stream, ensuring it is not null.
    /// </typeparam>
    /// <remarks>
    ///     This method is used to register a streaming request handler that processes a specific request and returns an
    ///     asynchronous stream of items. This enables efficient handling of large datasets without loading everything into
    ///     memory.
    /// </remarks>
    IDependencyInjectionBuilder RegisterStreamRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TStreamRequestHandler, TRequest,
        TItem>()
        where TStreamRequestHandler : class, IStreamRequestHandler<TRequest, TItem>
        where TItem : notnull
        where TRequest : IStreamRequest<TItem>;

    /// <summary>
    ///     Registers a request handler conditionally based on a predicate evaluated at service collection build time.
    /// </summary>
    /// <typeparam name="TRequestHandler">
    ///     The type of the request handler to be registered.
    /// </typeparam>
    /// <typeparam name="TRequest">
    ///     The type of the request being handled.
    /// </typeparam>
    /// <typeparam name="TResponse">
    ///     The type of the response returned by the handler.
    /// </typeparam>
    /// <param name="condition">
    ///     A predicate that determines whether the handler should be registered. Evaluated when building the service
    ///     collection.
    /// </param>
    /// <remarks>
    ///     Useful for registering handlers based on environment variables, feature flags, or configuration values.
    /// </remarks>
    IDependencyInjectionBuilder RegisterRequestHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TRequestHandler, TRequest,
        TResponse>(Func<bool> condition)
        where TRequestHandler : class, IRequestHandler<TRequest, TResponse>
        where TResponse : notnull
        where TRequest : IRequest<TResponse>;

    /// <summary>
    ///     Registers a request handler conditionally based on a predicate evaluated at service collection build time.
    /// </summary>
    /// <typeparam name="TRequestHandler">
    ///     The type of the request handler to be registered.
    /// </typeparam>
    /// <typeparam name="TRequest">
    ///     The type of the request being handled.
    /// </typeparam>
    /// <param name="condition">
    ///     A predicate that determines whether the handler should be registered. Evaluated when building the service
    ///     collection.
    /// </param>
    /// <remarks>
    ///     Useful for registering handlers based on environment variables, feature flags, or configuration values.
    /// </remarks>
    IDependencyInjectionBuilder RegisterRequestHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TRequestHandler, TRequest>(Func<bool> condition)
        where TRequestHandler : class, IRequestHandler<TRequest>
        where TRequest : IRequest;

    /// <summary>
    ///     Registers an event handler conditionally based on a predicate evaluated at service collection build time.
    /// </summary>
    /// <typeparam name="TEventHandler">
    ///     The type of the event handler to be registered.
    /// </typeparam>
    /// <typeparam name="TEvent">
    ///     The type of the event that the handler processes.
    /// </typeparam>
    /// <param name="condition">
    ///     A predicate that determines whether the handler should be registered. Evaluated when building the service
    ///     collection.
    /// </param>
    /// <remarks>
    ///     Useful for registering handlers based on environment variables, feature flags, or configuration values.
    /// </remarks>
    IDependencyInjectionBuilder RegisterEventHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventHandler, TEvent>(Func<bool> condition)
        where TEventHandler : class, IEventHandler<TEvent>
        where TEvent : class, IEvent;

    /// <summary>
    ///     Registers a streaming request handler conditionally based on a predicate evaluated at service collection build
    ///     time.
    /// </summary>
    /// <typeparam name="TStreamRequestHandler">
    ///     The type of the streaming request handler to be registered.
    /// </typeparam>
    /// <typeparam name="TRequest">
    ///     The type of the streaming request being handled.
    /// </typeparam>
    /// <typeparam name="TItem">
    ///     The type of items yielded by the stream.
    /// </typeparam>
    /// <param name="condition">
    ///     A predicate that determines whether the handler should be registered. Evaluated when building the service
    ///     collection.
    /// </param>
    /// <remarks>
    ///     Useful for registering handlers based on environment variables, feature flags, or configuration values.
    /// </remarks>
    IDependencyInjectionBuilder RegisterStreamRequestHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TStreamRequestHandler, TRequest,
        TItem>(Func<bool> condition)
        where TStreamRequestHandler : class, IStreamRequestHandler<TRequest, TItem>
        where TItem : notnull
        where TRequest : IStreamRequest<TItem>;

    /// <summary>
    ///     Registers a typed request pipeline behavior for a specific request type (without response).
    /// </summary>
    IDependencyInjectionBuilder RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest>()
        where TBehavior : class, IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest;

    /// <summary>
    ///     Registers a typed request pipeline behavior for a specific request/response pair.
    /// </summary>
    IDependencyInjectionBuilder RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest, TResponse>()
        where TBehavior : class, IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull;

    /// <summary>
    ///     Registers the CQRS boundary enforcement behavior for a specific request type (without response).
    ///     The behavior runs outermost via <see cref="IOrderedPipelineBehavior.First" />. The registration is
    ///     deduplicated, so the same request may be covered by more than one opted-in assembly's generated
    ///     RegisterGroup without registering the behavior twice.
    /// </summary>
    IDependencyInjectionBuilder RegisterCqrsBoundaryEnforcement<TRequest>()
        where TRequest : IRequest;

    /// <summary>
    ///     Registers the CQRS boundary enforcement behavior for a specific request/response pair. The behavior
    ///     runs outermost via <see cref="IOrderedPipelineBehavior.First" />. The registration is deduplicated;
    ///     see the no-response overload.
    /// </summary>
    IDependencyInjectionBuilder RegisterCqrsBoundaryEnforcement<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull;

    /// <summary>
    ///     Registers a typed event pipeline behavior for a specific event type.
    /// </summary>
    IDependencyInjectionBuilder RegisterEventPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TEvent>()
        where TBehavior : class, IEventPipelineBehavior<TEvent>
        where TEvent : class, IEvent;

    /// <summary>
    ///     Registers a typed stream request pipeline behavior for a specific request/item pair.
    /// </summary>
    IDependencyInjectionBuilder RegisterStreamRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest, TItem>()
        where TBehavior : class, IStreamRequestPipelineBehavior<TRequest, TItem>
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull;

    /// <summary>
    ///     Registers a request validator (no-response) together with the closed validation behavior that runs
    ///     it. The registration is deduplicated so the validation behavior runs at most once per request type
    ///     even when several validators target the same request.
    /// </summary>
    IDependencyInjectionBuilder RegisterValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TValidator, TRequest>()
        where TValidator : class, IRequestValidator<TRequest>
        where TRequest : IRequest;

    /// <summary>
    ///     Registers a request validator (with response) together with the closed validation behavior that
    ///     runs it. Deduplicated; see the no-response overload.
    /// </summary>
    IDependencyInjectionBuilder RegisterValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TValidator, TRequest, TResponse>()
        where TValidator : class, IRequestValidator<TRequest>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull;
}