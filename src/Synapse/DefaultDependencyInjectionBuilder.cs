using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse;

internal sealed class DefaultDependencyInjectionBuilder : IDependencyInjectionBuilder
{
    private readonly List<Action<IServiceCollection>> _actions = [];

    public IDependencyInjectionBuilder RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TRequestHandler, TRequest,
        TResponse>()
        where TRequestHandler : class, IRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        _actions.Add(services => services.RegisterRequestHandler<TRequestHandler, TRequest, TResponse>());
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
        return this;
    }

    public void Apply(IServiceCollection services)
    {
        foreach (var action in _actions) action(services);
    }
}