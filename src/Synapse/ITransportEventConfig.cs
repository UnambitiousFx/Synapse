namespace UnambitiousFx.Synapse;

/// <summary>
///     Represents the configuration of an event transport within the messaging infrastructure.
/// </summary>
public interface ITransportEventConfig
{
    /// Configures the current event as an external event to be sent through the transport system.
    /// External events are typically used to communicate with distributed systems or external services
    /// over a message transport mechanism.
    /// <returns>
    ///     The current instance of the transport configuration to allow method chaining.
    /// </returns>
    ITransportEventConfig AsExternal();

    /// Specifies that the event is a local event and will not be sent through the transport mechanism.
    /// Local events are intended to be processed within the local application only, without being
    /// propagated to external systems or services.
    /// <return>Returns the updated event configuration instance.</return>
    ITransportEventConfig AsLocal();

    /// <summary>
    ///     Configures the event to be treated as both external and local.
    ///     This allows the event to be processed within the local application
    ///     as well as being published to an external system.
    /// </summary>
    /// <returns>
    ///     Returns the current instance of <see cref="ITransportEventConfig" /> to allow for method chaining.
    /// </returns>
    ITransportEventConfig AsExternalAndLocal();
}