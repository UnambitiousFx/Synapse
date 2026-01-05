namespace UnambitiousFx.Synapse;

/// <summary>
///     Represents the configuration for a transport subscription, allowing customization of subscription behavior.
///     Implementations of this interface are used to define parameters
///     such as concurrency limits for event subscriptions.
/// </summary>
public interface ITransportSubscriptionConfig
{
    /// <summary>
    ///     Sets the maximum concurrency level for the processing of a subscription.
    /// </summary>
    /// <param name="maxConcurrency">
    ///     The maximum number of concurrent executions allowed for a subscription.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="ITransportSubscriptionConfig" /> to allow for method chaining.
    /// </returns>
    ITransportSubscriptionConfig SetMaxConcurrency(int maxConcurrency);
}