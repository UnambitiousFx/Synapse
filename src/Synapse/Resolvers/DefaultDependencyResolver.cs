using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions.Exceptions;

namespace UnambitiousFx.Synapse.Resolvers;

internal sealed class DefaultDependencyResolver : IDependencyResolver
{
    private readonly IServiceProvider _serviceProvider;

    public DefaultDependencyResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public TService? GetService<TService>()
        where TService : class
    {
        return _serviceProvider.GetService<TService>();
    }

    public TService GetRequiredService<TService>()
        where TService : class
    {
        return _serviceProvider.GetService<TService>() ?? throw new MissingHandlerException(typeof(TService));
    }

    public IEnumerable<TService> GetServices<TService>()
        where TService : class
    {
        return _serviceProvider.GetServices<TService>();
    }
}