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
    private readonly List<Action<IServiceCollection>> _actions = new();
    private readonly Dictionary<Type, DispatchEventDelegate> _eventDispatchers = new();
    private readonly Dictionary<Type, Delegate> _requestDispatchers = new();
    private readonly Dictionary<Type, Delegate> _streamRequestDispatchers = new();

    private DefaultDependencyInjectionBuilder _builder = new();

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private Type _contextFactory = typeof(DefaultContextFactory);

    private EmitMode _defaultPublisherMode = EmitMode.Now;

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private Type _eventOrchestrator = typeof(SequentialEventOrchestrator);

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private Type _eventOutBoxStorage = typeof(InMemoryEventOutboxStorage);

    private Action<OutboxOptions> _outboxConfigure = _ => { };


    public ISynapseConfig RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TRequestPipelineBehavior>()
        where TRequestPipelineBehavior : class, IRequestPipelineBehavior
    {
        _actions.Add(svc => svc.RegisterRequestPipelineBehavior<TRequestPipelineBehavior>());
        return this;
    }

    public ISynapseConfig RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TBehavior, TRequest>()
        where TBehavior : class, IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        _actions.Add(scv => scv.RegisterTypedRequestPipelineBehavior<TBehavior, TRequest>());
        return this;
    }

    public ISynapseConfig RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TBehavior, TRequest,
        TResponse>()
        where TBehavior : class, IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        _actions.Add(scv => scv.RegisterTypedRequestPipelineBehavior<TBehavior, TRequest, TResponse>());
        return this;
    }

    public ISynapseConfig RegisterConditionalRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TBehavior>(
        Func<object, bool> predicate)
        where TBehavior : class, IRequestPipelineBehavior
    {
        _actions.Add(scv => scv.RegisterConditionalRequestPipelineBehavior<TBehavior>(predicate));
        return this;
    }

    public ISynapseConfig RegisterConditionalRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TBehavior, TRequest>(
        Func<TRequest, bool> predicate)
        where TBehavior : class, IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        _actions.Add(scv =>
            scv.RegisterConditionalTypedRequestPipelineBehavior<TBehavior, TRequest>(predicate));
        return this;
    }

    public ISynapseConfig RegisterConditionalRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TBehavior, TRequest,
        TResponse>(Func<TRequest, bool> predicate)
        where TBehavior : class, IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        _actions.Add(scv =>
            scv.RegisterConditionalTypedRequestPipelineBehavior<TBehavior, TRequest, TResponse>(predicate));
        return this;
    }

    public ISynapseConfig RegisterEventPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TEventPipelineBehavior>()
        where TEventPipelineBehavior : class, IEventPipelineBehavior
    {
        _actions.Add(scv => scv.RegisterEventPipelineBehavior<TEventPipelineBehavior>());
        return this;
    }

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
        _builder = new DefaultDependencyInjectionBuilder();
        group.Register(_builder);
        foreach (var (type, dispatcher) in _builder.RequestDispatchers)
        {
            _requestDispatchers.TryAdd(type, dispatcher);
        }

        foreach (var (type, dispatcher) in _builder.EventDispatchers)
        {
            _eventDispatchers.TryAdd(type, dispatcher);
        }

        foreach (var (type, dispatcher) in _builder.StreamRequestDispatchers)
        {
            _streamRequestDispatchers.TryAdd(type, dispatcher);
        }

        return this;
    }

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
        return this;
    }

    public ISynapseConfig RegisterEventHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    THandler, TEvent>()
        where THandler : class, IEventHandler<TEvent>
        where TEvent : class, IEvent
    {
        _actions.Add(scv => scv.RegisterEventHandler<THandler, TEvent>());
        // Ensure only one dispatcher per event type; multiple handler registrations should not create duplicate dictionary entries
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

                // Ensure only one dispatcher per event type; multiple handler registrations should not create duplicate dictionary entries
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

    public ISynapseConfig EnableCqrsBoundaryEnforcement(bool enable = true)
    {
        if (!enable)
        {
            return this;
        }

        return RegisterRequestPipelineBehavior<CqrsBoundaryEnforcementBehavior>();
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

    public ISynapseConfig AddValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TValidator, TRequest>()
        where TValidator : class, IRequestValidator<TRequest>
        where TRequest : IRequest
    {
        _actions.Add(scv => scv.AddScoped<IRequestValidator<TRequest>, TValidator>());
        return this;
    }

    public ISynapseConfig AddValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TValidator, TRequest, TResponse>()
        where TValidator : class, IRequestValidator<TRequest>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        _actions.Add(scv => scv.AddScoped<IRequestValidator<TRequest>, TValidator>());
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

    public ISynapseConfig UseEventDispatcherRegistration<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TRegistration>()
        where TRegistration : class, IEventDispatcherRegistration, new()
    {
        var registration = new TRegistration();
        registration.RegisterDispatchers((type, dispatchDelegate) => { _eventDispatchers[type] = dispatchDelegate; });
        return this;
    }


    public void Apply()
    {
        _builder.Apply(services);

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
            opts.StreamRequestDispatchers = _streamRequestDispatchers;
        });
    }
}