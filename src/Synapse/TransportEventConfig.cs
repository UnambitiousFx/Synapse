using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish;

namespace UnambitiousFx.Synapse;

/// <summary>
///     Configures the transport behavior of an event in the messaging infrastructure.
/// </summary>
/// <typeparam name="TEvent">
///     The type of event being configured. This must be a class that implements <see cref="IEvent" />.
/// </typeparam>
public sealed class TransportEventConfig<TEvent> : ITransportEventConfig
    where TEvent : class, IEvent
{
    /// <summary>
    ///     Specifies the current mode of message distribution for an event transport configuration,
    ///     indicating whether events are handled locally, externally, or both.
    /// </summary>
    public DistributionMode DistributionMode { get; private set; }

    /// <summary>
    ///     A collection of actions that are used to configure the dependency injection container
    ///     for the given transport event configuration. Each action provides a specific
    ///     registration setup for services related to the event transport mechanism, such as
    ///     publishing traits or other necessary behaviors.
    /// </summary>
    public IEnumerable<Action<IServiceCollection>> Actions
    {
        get
        {
            yield return services =>
            {
                var trait = new PublishEventTrait<TEvent>
                {
                    DistributionMode = DistributionMode
                };
                services.AddSingleton<IPublishEventTrait>(trait);
            };
        }
    }

    /// <inheritdoc />
    public ITransportEventConfig AsExternal()
    {
        DistributionMode |= DistributionMode.External;
        return this;
    }

    /// <inheritdoc />
    public ITransportEventConfig AsLocal()
    {
        DistributionMode |= DistributionMode.Local;
        // Local events don't need messaging registration
        return this;
    }

    /// <inheritdoc />
    public ITransportEventConfig AsExternalAndLocal()
    {
        DistributionMode |= DistributionMode.LocalAndExternal;
        return this;
    }
}