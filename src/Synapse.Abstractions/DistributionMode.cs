namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents an uninitialized or default state for the message distribution mode.
/// </summary>
[Flags]
public enum DistributionMode
{
    /// <summary>
    ///     Indicates that the distribution mode is not specified or is in an uninitialized state.
    /// </summary>
    Undefined = 0,

    /// <summary>
    ///     Messages are processed only by local in-process handlers.
    /// </summary>
    Local = 1,

    /// <summary>
    ///     Messages are dispatched only to external transports, skipping local handlers.
    /// </summary>
    External = 2,

    /// <summary>
    ///     Messages are processed by both local handlers and external transports.
    /// </summary>
    LocalAndExternal = Local | External
}