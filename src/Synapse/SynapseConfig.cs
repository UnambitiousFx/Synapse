using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Pipelines;
using UnambitiousFx.Synapse.Publish;
using UnambitiousFx.Synapse.Publish.Orchestrators;
using UnambitiousFx.Synapse.Publish.Outbox;
using UnambitiousFx.Synapse.Resolvers;

namespace UnambitiousFx.Synapse;

internal sealed class SynapseConfig(IServiceCollection services) : ISynapseConfig
{
    private const string OpenGenericBehaviorAotMessage =
        "Open-generic pipeline behaviors require runtime code generation to close over their type arguments " +
        "and are not Native-AOT safe (value-type responses throw at resolution time). Decorate the behavior " +
        "with [PipelineBehavior] so the source generator emits closed registrations instead.";

    private readonly List<Action<IServiceCollection>> _actions = new();
    private readonly Dictionary<Type, DispatchEventDelegate> _eventDispatchers = new();
    private readonly Dictionary<Type, Delegate> _requestDispatchers = new();
    private readonly Dictionary<Type, Delegate> _voidRequestDispatchers = new();
    private readonly Dictionary<Type, Delegate> _streamRequestDispatchers = new();

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private Type _contextFactory = typeof(DefaultContextFactory);

    private EmitMode _defaultPublisherMode = EmitMode.Now;

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private Type _eventOrchestrator = typeof(SequentialEventOrchestrator);

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private Type _eventOutBoxStorage = typeof(InMemoryEventOutboxStorage);

    private Action<OutboxOptions> _outboxConfigure = _ => { };

    // ── Pipeline behaviors ───────────────────────────────────────────────────

    public ISynapseConfig RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TBehavior, TRequest>()
        where TBehavior : class, IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        _actions.Add(svc => svc.RegisterRequestPipelineBehavior<TBehavior, TRequest>());
        return this;
    }

    public ISynapseConfig RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TBehavior, TRequest, TResponse>()
        where TBehavior : class, IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        _actions.Add(svc => svc.RegisterRequestPipelineBehavior<TBehavior, TRequest, TResponse>());
        return this;
    }

    public ISynapseConfig RegisterEventPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TBehavior, TEvent>()
        where TBehavior : class, IEventPipelineBehavior<TEvent>
        where TEvent : class, IEvent
    {
        _actions.Add(svc => svc.RegisterEventPipelineBehavior<TBehavior, TEvent>());
        return this;
    }

    public ISynapseConfig RegisterStreamRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TBehavior, TRequest, TItem>()
        where TBehavior : class, IStreamRequestPipelineBehavior<TRequest, TItem>
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull
    {
        _actions.Add(svc => svc.RegisterStreamRequestPipelineBehavior<TBehavior, TRequest, TItem>());
        return this;
    }

    public ISynapseConfig RegisterCqrsBoundaryEnforcement<TRequest>()
        where TRequest : IRequest
    {
        _actions.Add(svc => svc.RegisterCqrsBoundaryEnforcement<TRequest>());
        return this;
    }

    public ISynapseConfig RegisterCqrsBoundaryEnforcement<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        _actions.Add(svc => svc.RegisterCqrsBoundaryEnforcement<TRequest, TResponse>());
        return this;
    }

    public ISynapseConfig AddOpenGenericRequestPipelineBehavior(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type openGenericBehaviorType)
    {
        return AddOpenGenericBehavior(typeof(IRequestPipelineBehavior<>), openGenericBehaviorType);
    }

    [RequiresDynamicCode(OpenGenericBehaviorAotMessage)]
    public ISynapseConfig AddOpenGenericRequestWithResponsePipelineBehavior(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type openGenericBehaviorType)
    {
        return AddOpenGenericBehavior(typeof(IRequestPipelineBehavior<,>), openGenericBehaviorType);
    }

    public ISynapseConfig AddOpenGenericEventPipelineBehavior(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type openGenericBehaviorType)
    {
        return AddOpenGenericBehavior(typeof(IEventPipelineBehavior<>), openGenericBehaviorType);
    }

    [RequiresDynamicCode(OpenGenericBehaviorAotMessage)]
    public ISynapseConfig AddOpenGenericStreamRequestPipelineBehavior(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type openGenericBehaviorType)
    {
        return AddOpenGenericBehavior(typeof(IStreamRequestPipelineBehavior<,>), openGenericBehaviorType);
    }

    private ISynapseConfig AddOpenGenericBehavior(
        Type pipelineInterfaceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type openGenericBehaviorType)
    {
        _actions.Add(svc => svc.AddScoped(pipelineInterfaceType, openGenericBehaviorType));
        return this;
    }

    // ── Handlers ────────────────────────────────────────────────────────────

    public ISynapseConfig RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    THandler, TRequest, TResponse>()
        where TResponse : notnull
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        _actions.Add(scv => scv.RegisterRequestHandler<THandler, TRequest, TResponse>());
        _requestDispatchers.TryAdd(typeof(TRequest),
            (Func<IRequest<TResponse>, IDependencyResolver, CancellationToken, ValueTask<Result<TResponse>>>)(
                (request, resolver, ct) =>
                {
                    var handler = resolver.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
                    return handler.HandleAsync((TRequest)request, ct);
                }));
        return this;
    }

    public ISynapseConfig RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    THandler, TRequest>()
        where TRequest : IRequest
        where THandler : class, IRequestHandler<TRequest>
    {
        _actions.Add(scv => scv.RegisterRequestHandler<THandler, TRequest>());
        _voidRequestDispatchers.TryAdd(typeof(TRequest),
            (Func<IRequest, IDependencyResolver, CancellationToken, ValueTask<Result>>)(
                (request, resolver, ct) =>
                {
                    var handler = resolver.GetRequiredService<IRequestHandler<TRequest>>();
                    return handler.HandleAsync((TRequest)request, ct);
                }));
        return this;
    }

    public ISynapseConfig RegisterEventHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    THandler, TEvent>()
        where THandler : class, IEventHandler<TEvent>
        where TEvent : class, IEvent
    {
        _actions.Add(scv => scv.RegisterEventHandler<THandler, TEvent>());
        _eventDispatchers.TryAdd(typeof(TEvent), (@event,
            dispatcher,
            cancellationToken) =>
        {
            if (@event is not TEvent typedEvent)
            {
                throw new InvalidOperationException(
                    $"Event type mismatch. Expected {typeof(TEvent)}, got {@event.GetType()}");
            }

            return dispatcher.DispatchAsync(typedEvent, cancellationToken);
        });
        return this;
    }

    public ISynapseConfig RegisterStreamRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    THandler, TRequest, TItem>()
        where TItem : notnull
        where TRequest : IStreamRequest<TItem>
        where THandler : class, IStreamRequestHandler<TRequest, TItem>
    {
        _actions.Add(svc => svc.RegisterStreamRequestHandler<THandler, TRequest, TItem>());
        _streamRequestDispatchers.TryAdd(typeof(TRequest),
            (Func<IStreamRequest<TItem>, IDependencyResolver, CancellationToken, IAsyncEnumerable<Result<TItem>>>)(
                (request, resolver, ct) =>
                {
                    var handler = resolver.GetRequiredService<IStreamRequestHandler<TRequest, TItem>>();
                    return handler.HandleAsync((TRequest)request, ct);
                }));
        return this;
    }

    // ── Conditional handler registration ────────────────────────────────────

    public ISynapseConfig RegisterRequestHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    THandler, TRequest, TResponse>(Func<bool> condition)
        where TResponse : notnull
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        _actions.Add(scv =>
        {
            if (condition())
            {
                scv.RegisterRequestHandler<THandler, TRequest, TResponse>();
                _requestDispatchers.TryAdd(typeof(TRequest),
                    (Func<IRequest<TResponse>, IDependencyResolver, CancellationToken, ValueTask<Result<TResponse>>>)(
                        (request, resolver, ct) =>
                        {
                            var handler = resolver.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
                            return handler.HandleAsync((TRequest)request, ct);
                        }));
            }
        });
        return this;
    }

    public ISynapseConfig RegisterRequestHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    THandler, TRequest>(Func<bool> condition)
        where TRequest : IRequest
        where THandler : class, IRequestHandler<TRequest>
    {
        _actions.Add(scv =>
        {
            if (condition())
            {
                scv.RegisterRequestHandler<THandler, TRequest>();
                _voidRequestDispatchers.TryAdd(typeof(TRequest),
                    (Func<IRequest, IDependencyResolver, CancellationToken, ValueTask<Result>>)(
                        (request, resolver, ct) =>
                        {
                            var handler = resolver.GetRequiredService<IRequestHandler<TRequest>>();
                            return handler.HandleAsync((TRequest)request, ct);
                        }));
            }
        });
        return this;
    }

    public ISynapseConfig RegisterEventHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    THandler, TEvent>(Func<bool> condition)
        where THandler : class, IEventHandler<TEvent>
        where TEvent : class, IEvent
    {
        _actions.Add(scv =>
        {
            if (condition())
            {
                scv.RegisterEventHandler<THandler, TEvent>();
                _eventDispatchers.TryAdd(typeof(TEvent), (@event,
                    dispatcher,
                    cancellationToken) =>
                {
                    if (@event is not TEvent typedEvent)
                    {
                        throw new InvalidOperationException(
                            $"Event type mismatch. Expected {typeof(TEvent)}, got {@event.GetType()}");
                    }

                    return dispatcher.DispatchAsync(typedEvent, cancellationToken);
                });
            }
        });
        return this;
    }

    // ── Infrastructure ───────────────────────────────────────────────────────

    public ISynapseConfig SetEventOrchestrator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TEventOrchestrator>()
        where TEventOrchestrator : class, IEventOrchestrator
    {
        _eventOrchestrator = typeof(TEventOrchestrator);
        return this;
    }

    public ISynapseConfig AddRegisterGroup(IRegisterGroup group)
    {
        var builder = new DefaultDependencyInjectionBuilder();
        group.Register(builder);

        // If the group also implements IEventDispatcherRegistration (e.g. the source-generated
        // RegisterGroup), register its dispatch delegates eagerly so the _eventDispatchers map
        // is populated before Apply() merges it into EventDispatcherOptions.
        if (group is IEventDispatcherRegistration registration)
        {
            registration.RegisterDispatchers((type, dispatch) => _eventDispatchers.TryAdd(type, dispatch));
        }

        _actions.Add(svc =>
        {
            builder.Apply(svc);

            foreach (var (type, dispatcher) in builder.RequestDispatchers)
            {
                _requestDispatchers.TryAdd(type, dispatcher);
            }

            foreach (var (type, dispatcher) in builder.VoidRequestDispatchers)
            {
                _voidRequestDispatchers.TryAdd(type, dispatcher);
            }

            foreach (var (type, dispatcher) in builder.EventDispatchers)
            {
                _eventDispatchers.TryAdd(type, dispatcher);
            }

            foreach (var (type, dispatcher) in builder.StreamRequestDispatchers)
            {
                _streamRequestDispatchers.TryAdd(type, dispatcher);
            }
        });

        return this;
    }

    public ISynapseConfig SetEventOutboxStorage<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TEventOutboxStorage>()
        where TEventOutboxStorage : class, IEventOutboxStorage
    {
        _eventOutBoxStorage = typeof(TEventOutboxStorage);
        return this;
    }

    public ISynapseConfig SetDefaultPublishingMode(EmitMode mode)
    {
        _defaultPublisherMode = mode;
        return this;
    }

    public ISynapseConfig ConfigureOutbox(Action<OutboxOptions> configure)
    {
        _outboxConfigure = configure;
        return this;
    }

    [Obsolete(
        "Runtime CQRS boundary enforcement was removed. Apply " +
        "[assembly: EnableSynapseCqrsBoundaryEnforcement] so the source generator emits closed " +
        "(Native-AOT safe) registrations. Calling this method with enable:true now throws.",
        error: true)]
    public ISynapseConfig EnableCqrsBoundaryEnforcement(bool enable = true)
    {
        if (!enable)
        {
            // Already disabled by default; nothing to do.
            return this;
        }

        // Fail loudly instead of silently dropping enforcement. The runtime registration path was
        // removed in favor of generator-emitted closed registrations gated on the assembly attribute;
        // re-registering here would reintroduce the open-generic value-type AOT failure (known-issue
        // 001) and duplicate the behavior, making CqrsBoundaryMetadata.Validate() throw on every request.
        throw new NotSupportedException(
            "Runtime CQRS boundary enforcement was removed. Apply " +
            "[assembly: EnableSynapseCqrsBoundaryEnforcement] so the Synapse source generator emits " +
            "closed (Native-AOT safe) CqrsBoundaryEnforcementBehavior registrations. The runtime " +
            "registration path was removed because open-generic behaviors cannot close over value-type " +
            "responses under Native AOT (see known-issue 001).");
    }

    public ISynapseConfig AddValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TValidator, TRequest>()
        where TValidator : class, IRequestValidator<TRequest>
        where TRequest : IRequest
    {
        _actions.Add(scv => scv.RegisterValidator<TValidator, TRequest>());
        return this;
    }

    public ISynapseConfig AddValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TValidator, TRequest, TResponse>()
        where TValidator : class, IRequestValidator<TRequest>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        _actions.Add(scv => scv.RegisterValidator<TValidator, TRequest, TResponse>());
        return this;
    }

    public ISynapseConfig UseDefaultContextFactory()
    {
        return UseContextFactory<DefaultContextFactory>();
    }

    public ISynapseConfig UseSlimContextFactory()
    {
        return UseContextFactory<SlimContextFactory>();
    }

    public ISynapseConfig UseContextFactory<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TContextFactory>()
        where TContextFactory : class, IContextFactory
    {
        _contextFactory = typeof(TContextFactory);
        return this;
    }

    public void Apply()
    {
        foreach (var action in _actions)
        {
            action(services);
        }

        services.AddSingleton(typeof(IEventOutboxStorage), _eventOutBoxStorage);
        services.AddScoped(typeof(IContextFactory), _contextFactory);
        services.AddScoped(typeof(IEventOrchestrator), _eventOrchestrator);

        services.Configure<PublisherOptions>(options => { options.DefaultMode = _defaultPublisherMode; });
        services.Configure<EventDispatcherOptions>(options =>
        {
            options.DispatchStrategy = DispatchStrategy.Immediate;
            if (options.Dispatchers.Count != 0)
            {
                options.Dispatchers = options.Dispatchers.Concat(_eventDispatchers)
                    .ToDictionary(x => x.Key, x => x.Value);
            }
            else
            {
                options.Dispatchers = _eventDispatchers;
            }
        });
        services.Configure<OutboxOptions>(options => { _outboxConfigure(options); });
        services.Configure<InvokerOptions>(opts =>
        {
            opts.RequestDispatchers = _requestDispatchers;
            opts.VoidRequestDispatchers = _voidRequestDispatchers;
            opts.StreamRequestDispatchers = _streamRequestDispatchers;
        });
    }
}
