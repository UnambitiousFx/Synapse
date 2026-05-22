using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Resolvers;

namespace UnambitiousFx.Synapse;

internal sealed class DefaultDependencyInjectionBuilder : IDependencyInjectionBuilder
{
    private readonly List<Action<IServiceCollection>> _actions = [];

    public readonly Dictionary<Type, Delegate> RequestDispatchers = new();
    public readonly Dictionary<Type, DispatchEventDelegate> EventDispatchers = new();
    public readonly Dictionary<Type, Delegate> StreamRequestDispatchers = new();

    public IDependencyInjectionBuilder RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TRequestHandler, TRequest,
        TResponse>()
        where TRequestHandler : class, IRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        _actions.Add(services => services.RegisterRequestHandler<TRequestHandler, TRequest, TResponse>());
        RequestDispatchers.TryAdd(typeof(TRequest),
            (Func<IRequest<TResponse>, IDependencyResolver, CancellationToken, ValueTask<Result<TResponse>>>)(
                (request, resolver, ct) =>
                {
                    var handler = resolver.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
                    return handler.HandleAsync((TRequest)request, ct);
                }));
        return this;
    }

    public IDependencyInjectionBuilder RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TRequestHandler, TRequest>()
        where TRequestHandler : class, IRequestHandler<TRequest>
        where TRequest : IRequest
    {
        _actions.Add(services => services.RegisterRequestHandler<TRequestHandler, TRequest>());
        return this;
    }

    public IDependencyInjectionBuilder RegisterEventHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TEventHandler, TEvent>()
        where TEventHandler : class, IEventHandler<TEvent>
        where TEvent : class, IEvent
    {
        _actions.Add(services => services.RegisterEventHandler<TEventHandler, TEvent>());
        EventDispatchers.TryAdd(typeof(TEvent), (@event, dispatcher, cancellationToken) =>
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

    public IDependencyInjectionBuilder RegisterStreamRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TStreamRequestHandler,
        TRequest, TItem>()
        where TStreamRequestHandler : class, IStreamRequestHandler<TRequest, TItem>
        where TItem : notnull
        where TRequest : IStreamRequest<TItem>
    {
        _actions.Add(services => services.RegisterStreamRequestHandler<TStreamRequestHandler, TRequest, TItem>());
        StreamRequestDispatchers.TryAdd(typeof(TRequest),
            (Func<IStreamRequest<TItem>, IDependencyResolver, CancellationToken, IAsyncEnumerable<Result<TItem>>>)(
                (request, resolver, ct) =>
                {
                    var handler = resolver.GetRequiredService<IStreamRequestHandler<TRequest, TItem>>();
                    return handler.HandleAsync((TRequest)request, ct);
                }));
        return this;
    }

    public IDependencyInjectionBuilder RegisterRequestHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TRequestHandler, TRequest,
        TResponse>(Func<bool> condition)
        where TRequestHandler : class, IRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        _actions.Add(services =>
        {
            if (condition())
            {
                services.RegisterRequestHandler<TRequestHandler, TRequest, TResponse>();
            }
        });
        return this;
    }

    public IDependencyInjectionBuilder RegisterRequestHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TRequestHandler, TRequest>(Func<bool> condition)
        where TRequestHandler : class, IRequestHandler<TRequest>
        where TRequest : IRequest
    {
        _actions.Add(services =>
        {
            if (condition())
            {
                services.RegisterRequestHandler<TRequestHandler, TRequest>();
            }
        });
        return this;
    }

    public IDependencyInjectionBuilder RegisterEventHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TEventHandler, TEvent>(Func<bool> condition)
        where TEventHandler : class, IEventHandler<TEvent>
        where TEvent : class, IEvent
    {
        _actions.Add(services =>
        {
            if (condition())
            {
                services.RegisterEventHandler<TEventHandler, TEvent>();
            }
        });
        return this;
    }

    public IDependencyInjectionBuilder RegisterStreamRequestHandlerWhen<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TStreamRequestHandler,
        TRequest, TItem>(Func<bool> condition)
        where TStreamRequestHandler : class, IStreamRequestHandler<TRequest, TItem>
        where TItem : notnull
        where TRequest : IStreamRequest<TItem>
    {
        _actions.Add(services =>
        {
            if (condition())
            {
                services.RegisterStreamRequestHandler<TStreamRequestHandler, TRequest, TItem>();
            }
        });
        return this;
    }

    public void Apply(IServiceCollection services)
    {
        foreach (var action in _actions)
        {
            action(services);
        }
    }
}