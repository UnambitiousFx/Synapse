using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse;

/// <summary>
///     Represents a configuration interface for transport-related settings within the mediator framework.
///     Enables the configuration of event behaviors and subscription settings for message dispatch and processing.
/// </summary>
public interface ITransportConfig
{
    /// <summary>
    ///     Configures an event to define whether it is treated as local, external, or both.
    /// </summary>
    /// <typeparam name="TEvent">
    ///     The type of the event being configured. This type must implement <see cref="IEvent" />
    ///     and have a parameterless constructor.
    /// </typeparam>
    /// <param name="config">
    ///     An optional action to configure the event behavior, allowing it to be marked
    ///     as external, local, or both.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="ITransportConfig" /> to allow method chaining.
    /// </returns>
    ITransportConfig ConfigureEvent<TEvent>(Action<ITransportEventConfig>? config = null)
        where TEvent : class, IEvent;

    /// <summary>
    ///     Subscribes to the specified event type for handling incoming event messages.
    ///     Allows configuration of the subscription settings, such as concurrency levels.
    /// </summary>
    /// <typeparam name="TEvent">
    ///     The type of the event to subscribe to. The event must implement the <see cref="IEvent" /> interface and
    ///     have a parameterless constructor.
    /// </typeparam>
    /// <param name="config">
    ///     An optional configuration action used to customize subscription settings via an
    ///     <see cref="ITransportSubscriptionConfig" /> instance.
    ///     If not provided, default settings will be applied.
    /// </param>
    /// <returns>
    ///     Returns the updated instance of the <see cref="ITransportConfig" /> interface to allow for chained operations.
    /// </returns>
    ITransportConfig Subscribe<TEvent>(Action<ITransportSubscriptionConfig>? config = null)
        where TEvent : class, IEvent;
}