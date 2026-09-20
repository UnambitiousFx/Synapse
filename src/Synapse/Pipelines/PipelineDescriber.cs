using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish;
using UnambitiousFx.Synapse.Resolvers;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Default <see cref="IPipelineDescriber" />: opens a scope per call, reads the pipeline from what the
///     container resolves, and disposes the scope. A proxied request reports <c>typeof(TRequestHandler)</c> and the
///     non-proxied fallback and events report <c>handler.GetType()</c>; they coincide for every registration Synapse
///     produces, but they are not interchangeable in general.
/// </summary>
internal sealed class PipelineDescriber : IPipelineDescriber
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PipelineDescriber(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public PipelineDescription? Describe<TRequest>()
        where TRequest : IRequest
    {
        using var scope = _scopeFactory.CreateScope();
        return DescribeHandler(scope.ServiceProvider.GetService<IRequestHandler<TRequest>>());
    }

    public PipelineDescription? Describe<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        using var scope = _scopeFactory.CreateScope();
        return DescribeHandler(scope.ServiceProvider.GetService<IRequestHandler<TRequest, TResponse>>());
    }

    public PipelineDescription? DescribeEvent<TEvent>()
        where TEvent : class, IEvent
    {
        using var scope = _scopeFactory.CreateScope();
        var (handlers, behaviors) =
            EventPipelineParts.Resolve<TEvent>(scope.ServiceProvider.GetRequiredService<IDependencyResolver>());

        if (handlers.Length == 0)
        {
            return null;
        }

        return new PipelineDescription(
            handlers.Select(handler => handler.GetType()).ToArray(),
            PipelineBehaviorOrdering.Describe(behaviors));
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

    [RequiresDynamicCode("Builds a generic method over the request type at runtime. Use the generic overloads under Native AOT.")]
    [RequiresUnreferencedCode("Looks up the generic overloads by reflection. Use the generic overloads when trimming.")]
    public PipelineDescription? Describe(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);

        // IRequest<TResponse> wins over IRequest; several IRequest<T> closures are ambiguous and the first is used.
        var responseType = requestType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IRequest<>))
            .Select(candidate => candidate.GetGenericArguments()[0])
            .FirstOrDefault();

        if (responseType is not null)
        {
            return InvokeGeneric(nameof(Describe), requestType, responseType);
        }

        if (typeof(IRequest).IsAssignableFrom(requestType))
        {
            return InvokeGeneric(nameof(Describe), requestType);
        }

        throw new ArgumentException(
            $"'{requestType}' does not implement IRequest or IRequest<TResponse>.", nameof(requestType));
    }

    [RequiresDynamicCode("Builds a generic method over the event type at runtime. Use the generic overloads under Native AOT.")]
    [RequiresUnreferencedCode("Looks up the generic overloads by reflection. Use the generic overloads when trimming.")]
    public PipelineDescription? DescribeEvent(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        if (eventType.IsValueType || !typeof(IEvent).IsAssignableFrom(eventType))
        {
            throw new ArgumentException($"'{eventType}' is not a reference type implementing IEvent.",
                nameof(eventType));
        }

        return InvokeGeneric(nameof(DescribeEvent), eventType);
    }

    // The generic overload is picked by name and arity, since Describe(Type) shares its name with them.
    [RequiresDynamicCode("Builds a generic method at runtime.")]
    [RequiresUnreferencedCode("Looks up the generic overloads by reflection.")]
    private PipelineDescription? InvokeGeneric(string name, params Type[] typeArguments)
    {
        var method = typeof(PipelineDescriber)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(candidate => candidate.Name == name &&
                                 candidate.IsGenericMethodDefinition &&
                                 candidate.GetGenericArguments().Length == typeArguments.Length)
            .MakeGenericMethod(typeArguments);

        // DoNotWrapExceptions so a throwing behavior constructor surfaces as itself, not as a TargetInvocationException.
        return (PipelineDescription?)method.Invoke(this, BindingFlags.DoNotWrapExceptions, null, null, null);
    }
}
