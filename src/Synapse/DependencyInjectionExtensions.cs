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
    public static IServiceCollection AddSynapse(this IServiceCollection services,
        Action<ISynapseConfig> configure)
    {
        var cfg = new SynapseConfig(services);
        configure(cfg);
        cfg.Apply();
        services.TryAddScoped<IDependencyResolver, DefaultDependencyResolver>();
        services.TryAddScoped<IOutboxManager, OutboxManager>();
        services.TryAddScoped<IOutboxCommit, OutboxCommit>();
        services.TryAddScoped<IEventDispatcher, EventDispatcher>();
        services.TryAddScoped<IInvoker, Invoker>();
        services.TryAddScoped<IEmitter, Emitter>();
        services.TryAddScoped<IContextFactory, DefaultContextFactory>();
        services.TryAddScoped(sp =>
        {
            var factory = sp.GetRequiredService<IContextFactory>();
            return new ContextHandler(factory);
        });
        services.TryAddScoped<IContextAccessor>(sp => sp.GetRequiredService<ContextHandler>());
        services.TryAddScoped<IContextSetter>(sp => sp.GetRequiredService<ContextHandler>());
        services.AddScoped<IContext>(sp => sp.GetRequiredService<IContextAccessor>().Context);

        services.TryAddSingleton<ISynapseMetrics>(sp =>
        {
            var meterFactory = sp.GetRequiredService<IMeterFactory>();
            var eventOutboxStorage = sp.GetService<IEventOutboxStorage>();
            return new SynapseMetrics(meterFactory, eventOutboxStorage);
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
        TBehavior, TRequest>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TBehavior : class, IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        services.Add(new ServiceDescriptor(typeof(IRequestPipelineBehavior<TRequest>), typeof(TBehavior), lifetime));
        return services;
    }

    internal static IServiceCollection RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest, TResponse>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TBehavior : class, IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        services.Add(
            new ServiceDescriptor(typeof(IRequestPipelineBehavior<TRequest, TResponse>), typeof(TBehavior), lifetime));
        return services;
    }

    internal static IServiceCollection RegisterEventPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TEvent>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TBehavior : class, IEventPipelineBehavior<TEvent>
        where TEvent : class, IEvent
    {
        services.Add(new ServiceDescriptor(typeof(IEventPipelineBehavior<TEvent>), typeof(TBehavior), lifetime));
        return services;
    }

    internal static IServiceCollection RegisterStreamRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest, TItem>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TBehavior : class, IStreamRequestPipelineBehavior<TRequest, TItem>
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull
    {
        services.Add(new ServiceDescriptor(typeof(IStreamRequestPipelineBehavior<TRequest, TItem>), typeof(TBehavior),
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
}
