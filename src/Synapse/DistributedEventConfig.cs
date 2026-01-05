using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse;

internal sealed class DistributedEventConfig : IDistributedEventConfig
{
    public List<Action<IServiceCollection>> Actions { get; } = [];

    public void Add(Action<IServiceCollection> configureServices)
    {
        Actions.Add(configureServices);
    }
}