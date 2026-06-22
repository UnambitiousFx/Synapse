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

    /// <summary>
    ///     Registers a user pipeline behavior (no-response). Deduplicated via <c>TryAddEnumerable</c> on
    ///     <c>(service type, effective implementation type)</c> — the implementation type is resolved across
    ///     by-type, instance, and typed-factory descriptors (see <see cref="EffectiveImplementationType" />) —
    ///     so the same closed behavior over the same request runs at most once however the descriptor was
    ///     built. This matters because an open-generic behavior is cross-producted against every handler in the
    ///     reference graph, so two opted-in assemblies' generated RegisterGroups can each emit the same closed
    ///     registration. A registration of the same behavior with a different <see cref="ServiceLifetime" />
    ///     is a conflict and throws rather than being silently dropped. See
    ///     <see cref="RegisterCqrsBoundaryEnforcement{TRequest}" /> for the parallel.
    /// </summary>
    internal static IServiceCollection RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TBehavior : class, IRequestPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        var serviceType = typeof(IRequestPipelineBehavior<TRequest>);
        var implementationType = typeof(TBehavior);
        ThrowOnLifetimeConflict(services, serviceType, implementationType, lifetime);
        services.TryAddEnumerable(new ServiceDescriptor(serviceType, implementationType, lifetime));
        return services;
    }

    /// <summary>
    ///     Registers a user pipeline behavior (with response). Deduplicated; see the no-response overload for why.
    /// </summary>
    internal static IServiceCollection RegisterRequestPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TRequest, TResponse>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TBehavior : class, IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        var serviceType = typeof(IRequestPipelineBehavior<TRequest, TResponse>);
        var implementationType = typeof(TBehavior);
        ThrowOnLifetimeConflict(services, serviceType, implementationType, lifetime);
        services.TryAddEnumerable(new ServiceDescriptor(serviceType, implementationType, lifetime));
        return services;
    }

    /// <summary>
    ///     Registers the CQRS boundary enforcement behavior (no-response variant). The behavior implements
    ///     <see cref="IOrderedPipelineBehavior" /> with <see cref="IOrderedPipelineBehavior.First" />, so it runs
    ///     outermost regardless of registration order. Deduplicated via <c>TryAddEnumerable</c> on
    ///     <c>(service type, effective implementation type)</c> across by-type, instance, and typed-factory
    ///     descriptors: a second registration of the same closed behavior is ignored. This matters because the
    ///     behavior is not idempotent (a duplicate would see the boundary marker set by the first instance and
    ///     throw on every request), and the same request can be covered by more than one opted-in assembly's
    ///     generated RegisterGroup once enforcement propagates across assemblies. A registration with a
    ///     conflicting <see cref="ServiceLifetime" /> throws.
    /// </summary>
    internal static IServiceCollection RegisterCqrsBoundaryEnforcement<TRequest>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TRequest : IRequest
    {
        var serviceType = typeof(IRequestPipelineBehavior<TRequest>);
        var implementationType = typeof(CqrsBoundaryEnforcementBehavior<TRequest>);
        ThrowOnLifetimeConflict(services, serviceType, implementationType, lifetime);
        services.TryAddEnumerable(new ServiceDescriptor(serviceType, implementationType, lifetime));
        return services;
    }

    /// <summary>
    ///     Registers the CQRS boundary enforcement behavior (with-response variant). The behavior runs
    ///     outermost via <see cref="IOrderedPipelineBehavior.First" />. Deduplicated; see the no-response overload.
    /// </summary>
    internal static IServiceCollection RegisterCqrsBoundaryEnforcement<TRequest, TResponse>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        var serviceType = typeof(IRequestPipelineBehavior<TRequest, TResponse>);
        var implementationType = typeof(CqrsBoundaryEnforcementBehavior<TRequest, TResponse>);
        ThrowOnLifetimeConflict(services, serviceType, implementationType, lifetime);
        services.TryAddEnumerable(new ServiceDescriptor(serviceType, implementationType, lifetime));
        return services;
    }

    /// <summary>
    ///     Resolves the implementation type a descriptor will produce, regardless of how it was constructed:
    ///     an implementation type, an instance (its runtime type), or a typed factory
    ///     (<c>Func&lt;IServiceProvider, TImpl&gt;</c>, read from the delegate's return type). Mirrors the
    ///     framework's internal <c>ServiceDescriptor.GetImplementationType</c> so dedup matches what
    ///     <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)" />
    ///     does. Returns <c>null</c> when the type is undeterminable — a factory whose declared return type is
    ///     <see cref="object" /> — or for keyed descriptors (behaviors are never keyed, and property access on a
    ///     keyed descriptor throws).
    /// </summary>
    private static Type? EffectiveImplementationType(ServiceDescriptor descriptor)
    {
        if (descriptor.IsKeyedService)
        {
            return null;
        }

        if (descriptor.ImplementationType is { } implementationType)
        {
            return implementationType;
        }

        if (descriptor.ImplementationInstance is { } instance)
        {
            return instance.GetType();
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            // Func<IServiceProvider, TResult> — the second type argument is the produced type.
            var arguments = factory.GetType().GenericTypeArguments;
            if (arguments.Length == 2 && arguments[1] != typeof(object))
            {
                return arguments[1];
            }
        }

        return null;
    }

    /// <summary>
    ///     Throws when the same behavior (matched on service type + <see cref="EffectiveImplementationType" />)
    ///     is already registered with a different <see cref="ServiceLifetime" />. Dedup is type-identity based,
    ///     so a lifetime conflict cannot be reconciled by silently keeping one — it is surfaced instead. The
    ///     builder surface always registers <see cref="ServiceLifetime.Scoped" />, so a conflict can only arise
    ///     from user code adding the same behavior to the collection with a different lifetime.
    /// </summary>
    private static void ThrowOnLifetimeConflict(IServiceCollection services, Type serviceType,
        Type implementationType, ServiceLifetime lifetime)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType != serviceType ||
                EffectiveImplementationType(descriptor) != implementationType ||
                descriptor.Lifetime == lifetime)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Pipeline behavior '{implementationType}' for service '{serviceType}' is already registered " +
                $"with lifetime '{descriptor.Lifetime}', which conflicts with the requested '{lifetime}'. " +
                "Register the behavior with a single, consistent lifetime.");
        }
    }

    internal static IServiceCollection RegisterEventPipelineBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior, TEvent>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TBehavior : class, IEventPipelineBehavior<TEvent>
        where TEvent : class, IEvent
    {
        var serviceType = typeof(IEventPipelineBehavior<TEvent>);
        var implementationType = typeof(TBehavior);
        ThrowOnLifetimeConflict(services, serviceType, implementationType, lifetime);
        services.TryAddEnumerable(new ServiceDescriptor(serviceType, implementationType, lifetime));
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
        var serviceType = typeof(IStreamRequestPipelineBehavior<TRequest, TItem>);
        var implementationType = typeof(TBehavior);
        ThrowOnLifetimeConflict(services, serviceType, implementationType, lifetime);
        services.TryAddEnumerable(new ServiceDescriptor(serviceType, implementationType, lifetime));
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

    /// <summary>
    ///     Registers a request validator (no-response) together with the closed
    ///     <see cref="RequestValidationBehavior{TRequest}" /> that runs it. Both registrations are
    ///     deduplicated with <c>TryAddEnumerable</c>: the validation behavior runs at most once per request
    ///     type (it resolves all <see cref="IRequestValidator{TRequest}" /> instances in a single pass), and a
    ///     validator wired by both the source generator's <c>[Validator]</c> attribute and a runtime
    ///     <c>AddValidator</c> call is added only once.
    /// </summary>
    internal static IServiceCollection RegisterValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TValidator, TRequest>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TValidator : class, IRequestValidator<TRequest>
        where TRequest : IRequest
    {
        services.TryAddEnumerable(
            new ServiceDescriptor(typeof(IRequestValidator<TRequest>), typeof(TValidator), lifetime));
        ThrowOnLifetimeConflict(services, typeof(IRequestPipelineBehavior<TRequest>),
            typeof(RequestValidationBehavior<TRequest>), lifetime);
        services.TryAddEnumerable(new ServiceDescriptor(typeof(IRequestPipelineBehavior<TRequest>),
            typeof(RequestValidationBehavior<TRequest>), lifetime));
        return services;
    }

    /// <summary>
    ///     Registers a request validator (with response) together with the closed
    ///     <see cref="RequestValidationBehavior{TRequest, TResponse}" /> that runs it. Both registrations are
    ///     deduplicated; see the no-response overload for why.
    /// </summary>
    internal static IServiceCollection RegisterValidator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TValidator, TRequest, TResponse>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TValidator : class, IRequestValidator<TRequest>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        services.TryAddEnumerable(
            new ServiceDescriptor(typeof(IRequestValidator<TRequest>), typeof(TValidator), lifetime));
        ThrowOnLifetimeConflict(services, typeof(IRequestPipelineBehavior<TRequest, TResponse>),
            typeof(RequestValidationBehavior<TRequest, TResponse>), lifetime);
        services.TryAddEnumerable(new ServiceDescriptor(typeof(IRequestPipelineBehavior<TRequest, TResponse>),
            typeof(RequestValidationBehavior<TRequest, TResponse>), lifetime));
        return services;
    }
}
