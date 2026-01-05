using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Observability;
using UnambitiousFx.Synapse.Pipelines;
using UnambitiousFx.Synapse.Publish;
using UnambitiousFx.Synapse.Publish.Outbox;
using UnambitiousFx.Synapse.Resolvers;
using UnambitiousFx.Synapse.Send;

namespace UnambitiousFx.Synapse;

/// <summary>
///     Provides extension methods for registering mediator services and related components
///     within an <see cref="IServiceCollection" />.
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    ///     Adds the mediator services to the specified IServiceCollection.
    /// </summary>
    /// <param name="services">The service collection to add the mediator services to.</param>
    /// <param name="configure">A delegate to configure the mediator services.</param>
    /// <returns>The IServiceCollection with the mediator services added.</returns>
    public static IServiceCollection AddMediator(this IServiceCollection services,
        Action<IMediatorConfig> configure)
    {
        var cfg = new MediatorConfig(services);
        configure(cfg);
        cfg.Apply();
        services.TryAddScoped<IDependencyResolver, DefaultDependencyResolver>();
        services.TryAddScoped<IOutboxManager, OutboxManager>();
        services.TryAddScoped<IOutboxCommit, OutboxCommit>();
        services.TryAddScoped<IEventDispatcher, EventDispatcher>();
        services.TryAddScoped<ISender, Sender>();
        services.TryAddScoped<IPublisher, Publisher>();
        services.TryAddScoped<IContextFactory, DefaultContextFactory>();
        services.TryAddScoped<IContextAccessor>(sp =>
        {
            var factory = sp.GetRequiredService<IContextFactory>();
            return new ContextAccessor(factory);
        });
        services.AddScoped<IContext>(sp => sp.GetRequiredService<IContextAccessor>()
            .Context);

        services.TryAddSingleton<IMediatorMetrics>(sp =>
        {
            var meterFactory = sp.GetRequiredService<IMeterFactory>();
            var eventOutboxStorage = sp.GetService<IEventOutboxStorage>();
            return new MediatorMetrics(meterFactory, eventOutboxStorage);
        });

        return services.AddMetrics();
    }

    internal static IServiceCollection RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TRequest, TResponse>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TResponse : notnull
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        services.Add(new ServiceDescriptor(typeof(THandler), typeof(THandler), lifetime));
        services.Add(new ServiceDescriptor(typeof(IRequestHandler<TRequest, TResponse>),
            typeof(ProxyRequestHandler<THandler, TRequest, TResponse>), lifetime));
        return services;
    }

    internal static IServiceCollection RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TRequest>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TRequest : IRequest
        where THandler : class, IRequestHandler<TRequest>
    {
        services.Add(new ServiceDescriptor(typeof(THandler), typeof(THandler), lifetime));
        services.Add(new ServiceDescriptor(typeof(IRequestHandler<TRequest>),
            typeof(ProxyRequestHandler<THandler, TRequest>), lifetime));
        return services;
    }

    internal static IServiceCollection RegisterEventHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TEvent>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where THandler : class, IEventHandler<TEvent>
        where TEvent : class, IEvent
    {
        services.Add(new ServiceDescriptor(typeof(IEventHandler<TEvent>), typeof(THandler), lifetime));
        return services;
    }

    internal static IServiceCollection RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TRequestPipelineBehavior>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TRequestPipelineBehavior : class, IRequestPipelineBehavior
    {
        services.Add(
            new ServiceDescriptor(typeof(IRequestPipelineBehavior), typeof(TRequestPipelineBehavior), lifetime));
        return services;
    }

    internal static IServiceCollection RegisterEventPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TEventPipelineBehavior>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TEventPipelineBehavior : class, IEventPipelineBehavior
    {
        services.Add(new ServiceDescriptor(typeof(IEventPipelineBehavior), typeof(TEventPipelineBehavior), lifetime));
        return services;
    }

    internal static IServiceCollection RegisterTypedRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TBehavior : class, IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        services.Add(new ServiceDescriptor(typeof(TBehavior), typeof(TBehavior), lifetime));
        services.Add(new ServiceDescriptor(typeof(IRequestPipelineBehavior),
            sp => new RequestTypedBehaviorAdapter<TRequest>(sp.GetRequiredService<TBehavior>()), lifetime));
        return services;
    }

    internal static IServiceCollection RegisterTypedRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest,
        TResponse>(this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TBehavior : class, IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        services.Add(new ServiceDescriptor(typeof(TBehavior), typeof(TBehavior), lifetime));
        services.Add(new ServiceDescriptor(typeof(IRequestPipelineBehavior),
            sp => new RequestTypedBehaviorAdapter<TRequest, TResponse>(sp.GetRequiredService<TBehavior>()),
            lifetime));
        return services;
    }

    internal static IServiceCollection RegisterConditionalRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior>(
        this IServiceCollection services,
        Func<object, bool> predicate,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TBehavior : class, IRequestPipelineBehavior
    {
        services.Add(new ServiceDescriptor(typeof(TBehavior), typeof(TBehavior), lifetime));
        services.Add(new ServiceDescriptor(typeof(IRequestPipelineBehavior),
            sp => new ConditionalUntypedBehaviorWrapper(sp.GetRequiredService<TBehavior>(), predicate), lifetime));
        return services;
    }

    internal static IServiceCollection RegisterConditionalTypedRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior,
        TRequest>(this IServiceCollection services,
        Func<TRequest, bool> predicate,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TBehavior : class, IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        services.Add(new ServiceDescriptor(typeof(TBehavior), typeof(TBehavior), lifetime));
        services.Add(new ServiceDescriptor(typeof(IRequestPipelineBehavior), sp => new ConditionalTypedBehaviorWrapper(
            new RequestTypedBehaviorAdapter<TRequest>(sp.GetRequiredService<TBehavior>()),
            o => o is TRequest r && predicate(r)), lifetime));
        return services;
    }

    internal static IServiceCollection RegisterConditionalTypedRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior,
        TRequest, TResponse>(this IServiceCollection services,
        Func<TRequest, bool> predicate,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TBehavior : class, IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        services.Add(new ServiceDescriptor(typeof(TBehavior), typeof(TBehavior), lifetime));
        services.Add(new ServiceDescriptor(typeof(IRequestPipelineBehavior), sp => new ConditionalTypedBehaviorWrapper(
                new RequestTypedBehaviorAdapter<TRequest, TResponse>(sp.GetRequiredService<TBehavior>()),
                o => o is TRequest r && predicate(r)),
            lifetime));
        return services;
    }

    internal static IServiceCollection RegisterStreamRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler, TRequest, TItem>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TItem : notnull
        where TRequest : IStreamRequest<TItem>
        where THandler : class, IStreamRequestHandler<TRequest, TItem>
    {
        services.Add(new ServiceDescriptor(typeof(THandler), typeof(THandler), lifetime));
        services.Add(new ServiceDescriptor(typeof(IStreamRequestHandler<TRequest, TItem>),
            typeof(ProxyStreamRequestHandler<THandler, TRequest, TItem>), lifetime));
        return services;
    }

    internal static IServiceCollection RegisterStreamRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TStreamPipelineBehavior>(this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TStreamPipelineBehavior : class, IStreamRequestPipelineBehavior
    {
        services.Add(new ServiceDescriptor(typeof(IStreamRequestPipelineBehavior), typeof(TStreamPipelineBehavior),
            lifetime));
        return services;
    }
}