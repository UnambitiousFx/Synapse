using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Pipelines;
using UnambitiousFx.Synapse.Publish;
using UnambitiousFx.Synapse.Publish.Orchestrators;
using UnambitiousFx.Synapse.Publish.Outbox;

namespace UnambitiousFx.Synapse;

internal sealed class MediatorConfig(IServiceCollection services) : IMediatorConfig
{
    private readonly List<Action<IServiceCollection>> _actions = new();
    private readonly Dictionary<Type, DispatchEventDelegate> _eventDispatchers = new();

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private readonly Type _transportDispatcher = typeof(NoopTransportDispatcher);

    private DefaultDependencyInjectionBuilder _builder = new();

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private Type _contextFactory = typeof(DefaultContextFactory);

    private PublishMode _defaultPublisherMode = PublishMode.Now;

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private Type _eventOrchestrator = typeof(SequentialEventOrchestrator);

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private Type _eventOutBoxStorage = typeof(InMemoryEventOutboxStorage);

    private Action<OutboxOptions> _outboxConfigure = _ => { };


    public IMediatorConfig RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TRequestPipelineBehavior>()
        where TRequestPipelineBehavior : class, IRequestPipelineBehavior
    {
        _actions.Add(svc => svc.RegisterRequestPipelineBehavior<TRequestPipelineBehavior>());
        return this;
    }

    public IMediatorConfig RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest>()
        where TBehavior : class, IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        _actions.Add(scv => scv.RegisterTypedRequestPipelineBehavior<TBehavior, TRequest>());
        return this;
    }

    public IMediatorConfig RegisterRequestPipelineBehavior<
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

    public IMediatorConfig RegisterConditionalRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior>(
        Func<object, bool> predicate)
        where TBehavior : class, IRequestPipelineBehavior
    {
        _actions.Add(scv => scv.RegisterConditionalRequestPipelineBehavior<TBehavior>(predicate));
        return this;
    }

    public IMediatorConfig RegisterConditionalRequestPipelineBehavior<
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

    public IMediatorConfig RegisterConditionalRequestPipelineBehavior<
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

    public IMediatorConfig RegisterEventPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventPipelineBehavior>()
        where TEventPipelineBehavior : class, IEventPipelineBehavior
    {
        _actions.Add(scv => scv.RegisterEventPipelineBehavior<TEventPipelineBehavior>());
        return this;
    }

    public IMediatorConfig SetEventOrchestrator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventOrchestrator>()
        where TEventOrchestrator : class, IEventOrchestrator
    {
        _eventOrchestrator = typeof(TEventOrchestrator);
        return this;
    }

    public IMediatorConfig AddRegisterGroup(IRegisterGroup group)
    {
        _builder = new DefaultDependencyInjectionBuilder();
        group.Register(_builder);
        return this;
    }

    public IMediatorConfig RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TRequest, TResponse>()
        where TResponse : notnull
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        _actions.Add(scv => scv.RegisterRequestHandler<THandler, TRequest, TResponse>());
        return this;
    }

    public IMediatorConfig RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TRequest>()
        where TRequest : IRequest
        where THandler : class, IRequestHandler<TRequest>
    {
        _actions.Add(scv => scv.RegisterRequestHandler<THandler, TRequest>());
        return this;
    }

    public IMediatorConfig RegisterEventHandler<
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
                throw new InvalidOperationException(
                    $"Event type mismatch. Expected {typeof(TEvent)}, got {@event.GetType()}");
            return dispatcher.DispatchAsync(typedEvent, DistributionMode.Undefined, cancellationToken);
        });
        return this;
    }

    public IMediatorConfig SetEventOutboxStorage<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventOutboxStorage>()
        where TEventOutboxStorage : class, IEventOutboxStorage
    {
        _eventOutBoxStorage = typeof(TEventOutboxStorage);
        return this;
    }

    public IMediatorConfig SetDefaultPublishingMode(PublishMode mode)
    {
        _defaultPublisherMode = mode;
        return this;
    }

    public IMediatorConfig ConfigureOutbox(Action<OutboxOptions> configure)
    {
        _outboxConfigure = configure;
        return this;
    }

    public IMediatorConfig EnableCqrsBoundaryEnforcement(bool enable = true)
    {
        if (!enable) return this;

        return RegisterRequestPipelineBehavior<CqrsBoundaryEnforcementBehavior>();
    }

    public IMediatorConfig AddValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TValidator, TRequest>()
        where TValidator : class, IRequestValidator<TRequest>
        where TRequest : IRequest
    {
        _actions.Add(scv => scv.AddScoped<IRequestValidator<TRequest>, TValidator>());
        return this;
    }

    public IMediatorConfig UseDefaultContextFactory()
    {
        return UseContextFactory<DefaultContextFactory>();
    }

    public IMediatorConfig UseSlimContextFactory()
    {
        return UseContextFactory<SlimContextFactory>();
    }

    public IMediatorConfig UseContextFactory<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TContextFactory>()
        where TContextFactory : class, IContextFactory
    {
        _contextFactory = typeof(TContextFactory);
        return this;
    }


    public IMediatorConfig RegisterEventRoutingFilter<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventRoutingFilter>()
        where TEventRoutingFilter : class, IEventRoutingFilter
    {
        _actions.Add(scv => scv.AddScoped<IEventRoutingFilter, TEventRoutingFilter>());
        return this;
    }

    public IMediatorConfig UseEventDispatcherRegistration<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TRegistration>()
        where TRegistration : class, IEventDispatcherRegistration, new()
    {
        var registration = new TRegistration();
        registration.RegisterDispatchers((type, del) =>
        {
            if (del is DispatchEventDelegate dispatchDelegate) _eventDispatchers[type] = dispatchDelegate;
        });
        return this;
    }

    public IMediatorConfig EnableDistributedEvent(Func<IDistributedEventConfig, ITransportConfig> defineTransport,
        Action<ITransportConfig> configureTransport)
    {
        var transportConfig = new DistributedEventConfig();
        var transport = defineTransport(transportConfig);
        configureTransport(transport);
        _actions.AddRange(transportConfig.Actions);
        return this;
    }

    public void Apply()
    {
        _builder.Apply(services);

        foreach (var action in _actions) action(services);

        services.AddSingleton(typeof(IEventOutboxStorage), _eventOutBoxStorage);
        services.AddScoped(typeof(IContextFactory), _contextFactory);
        services.AddScoped(typeof(IEventOrchestrator), _eventOrchestrator);
        services.AddScoped(typeof(ITransportDispatcher), _transportDispatcher);

        services.Configure<PublisherOptions>(options => { options.DefaultMode = _defaultPublisherMode; });
        services.Configure<EventDispatcherOptions>(options =>
        {
            options.DefaultDistributionMode = DistributionMode.Local;
            options.DispatchStrategy = DispatchStrategy.Immediate;
            options.Dispatchers = _eventDispatchers;
        });
        services.Configure<OutboxOptions>(options => { _outboxConfigure(options); });
    }
}