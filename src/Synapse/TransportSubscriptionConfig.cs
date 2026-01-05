using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish;

namespace UnambitiousFx.Synapse;

/// <summary>
///     Represents a configuration object for managing subscription settings for a specific event type in a transport
///     layer.
///     This class is responsible for specifying concurrency limits and registering event handlers for the associated event
///     type.
/// </summary>
/// <typeparam name="TEvent">
///     The type of event for which the subscription configuration is defined. Must implement the <see cref="IEvent" />
///     interface.
/// </typeparam>
public sealed class TransportSubscriptionConfig<TEvent> : ITransportSubscriptionConfig
    where TEvent : class, IEvent
{
    /// <summary>
    ///     Gets the maximum level of concurrency allowed for processing events in the transport subscription.
    ///     This property determines the upper limit on the number of concurrent handlers that can process
    ///     events of the specified type. Configuring this value allows fine-grained control of resource
    ///     utilization and throughput in event subscription scenarios.
    /// </summary>
    public int MaxConcurrency { get; private set; }

    /// <summary>
    ///     Provides a collection of actions used to configure services in the dependency injection container
    ///     for a transport subscription. These actions are executed to setup the necessary service registrations
    ///     and traits required for the subscription's functionality, such as handling events with specific behavior.
    /// </summary>
    public IEnumerable<Action<IServiceCollection>> Actions
    {
        get
        {
            yield return services =>
            {
                services.AddScoped<ISubscribeEventTrait>(sp =>
                {
                    var handler = sp.GetRequiredService<IEventHandler<TEvent>>();
                    return new SubscribeEventTrait<TEvent>(handler)
                    {
                        MaxConcurrency = MaxConcurrency
                    };
                });
            };
        }
    }

    /// <inheritdoc />
    public ITransportSubscriptionConfig SetMaxConcurrency(int maxConcurrency)
    {
        MaxConcurrency = maxConcurrency;
        return this;
    }
}