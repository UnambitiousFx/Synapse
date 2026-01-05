namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Specifies that the event should be published immediately without any delay or queuing mechanism.
/// </summary>
public enum PublishMode
{
    /// <summary>
    ///     Represents the default publishing mode for events, determined by the mediator's configuration or system default.
    /// </summary>
    Default = 0,

    /// <summary>
    ///     Specifies that the event should be published immediately without any delay or queuing mechanism.
    /// </summary>
    Now = 1,

    /// <summary>
    ///     Specifies that events should be published using an outbox pattern, ensuring eventual consistency by storing the
    ///     events
    ///     in a persistent outbox before processing or delivering them to their intended destinations.
    /// </summary>
    Outbox = 2
}