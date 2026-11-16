using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish;
using UnambitiousFx.Synapse.Resolvers;
using UnambitiousFx.Synapse.Validation;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Default <see cref="IPipelineDescriber" />: opens a scope per call, reads the pipeline from what the
///     container resolves, and disposes the scope. A proxied request reports <c>typeof(TRequestHandler)</c> and the
///     non-proxied fallback and events report <c>handler.GetType()</c>; they coincide for every registration Synapse
///     produces, but they are not interchangeable in general.
/// </summary>
internal sealed class PipelineDescriber : IPipelineDescriber
{
    private readonly IServiceProvider _provider;
    private readonly IServiceScopeFactory _scopeFactory;

    // The registry is resolved per call rather than injected: it is a singleton added by AddSynapse, and resolving
    // it lazily keeps the describer constructible on its own.
    public PipelineDescriber(IServiceScopeFactory scopeFactory, IServiceProvider provider)
    {
        _scopeFactory = scopeFactory;
        _provider = provider;
    }

    public PipelineDescription? Describe<TRequest>()
        where TRequest : IRequest
    {
        // The API is synchronous and validation runs on a dedicated thread without a synchronization context, so
        // the async scope (which tolerates async-only disposables) is disposed by blocking on DisposeAsync.
        var scope = _scopeFactory.CreateAsyncScope();
        try
        {
            return DescribeHandler(scope.ServiceProvider.GetService<IRequestHandler<TRequest>>());
        }
        finally
        {
            scope.DisposeSynchronously();
        }
    }

    public PipelineDescription? Describe<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        var scope = _scopeFactory.CreateAsyncScope();
        try
        {
            return DescribeHandler(scope.ServiceProvider.GetService<IRequestHandler<TRequest, TResponse>>());
        }
        finally
        {
            scope.DisposeSynchronously();
        }
    }

    public PipelineDescription? DescribeEvent<TEvent>()
        where TEvent : class, IEvent
    {
        var scope = _scopeFactory.CreateAsyncScope();
        try
        {
            var (handlers, behaviors) =
                EventPipelineParts.Resolve<TEvent>(scope.ServiceProvider.GetRequiredService<IDependencyResolver>());

            if (handlers.Length == 0)
            {
                return null;
            }

            return new PipelineDescription(
                handlers.Select(handler => handler.GetType()).ToArray(),
                PipelineBehaviorOrdering.Describe(behaviors,
                    scope.ServiceProvider.GetService<PipelineOrdering>() ?? PipelineOrdering.Empty));
        }
        finally
        {
            scope.DisposeSynchronously();
        }
    }

    private static PipelineDescription? DescribeHandler(object? handler)
    {
        return handler switch
        {
            null => null,
            IPipelineInfo info => new PipelineDescription([info.HandlerType], info.Behaviors),
            // Registered straight into the container rather than through Synapse: nothing wraps it, so no
            // behavior can apply.
            _ => new PipelineDescription([handler.GetType()], [])
        };
    }

    public PipelineDescription? Describe(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        if (!typeof(IRequest).IsAssignableFrom(requestType) && !ImplementsTypedRequest(requestType))
        {
            throw new ArgumentException(
                $"'{requestType}' does not implement IRequest or IRequest<TResponse>.", nameof(requestType));
        }

        var registry = _provider.GetService<SynapseRegistry>();
        return registry is not null && registry.RequestProbes.TryGetValue(requestType, out var probe)
            ? probe(this)
            : null;
    }

    public PipelineDescription? DescribeEvent(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        if (eventType.IsValueType || !typeof(IEvent).IsAssignableFrom(eventType))
        {
            throw new ArgumentException($"'{eventType}' is not a reference type implementing IEvent.",
                nameof(eventType));
        }

        var registry = _provider.GetService<SynapseRegistry>();
        return registry is not null && registry.EventProbes.TryGetValue(eventType, out var probe)
            ? probe(this)
            : null;
    }

    // A registered request type has its interfaces rooted by the closed handler registration, so trimming can only
    // thin out the interfaces of an unregistered type, which turns an answer of null into an ArgumentException.
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Only affects unregistered types; see the comment above.")]
    private static bool ImplementsTypedRequest(Type type)
    {
        foreach (var candidate in type.GetInterfaces())
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IRequest<>))
            {
                return true;
            }
        }

        return false;
    }
}
