namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a factory responsible for creating <see cref="IContext" /> instances.
/// </summary>
public interface IContextFactory
{
    /// <summary>
    ///     Creates a new <see cref="IContext" />, adopting whatever flow state arrived from an inbound
    ///     boundary.
    /// </summary>
    /// <param name="inbound">
    ///     Flow state extracted at the boundary, or <see cref="PropagatedContext.None" /> when the unit of
    ///     work starts in this process.
    /// </param>
    /// <returns>A new instance of <see cref="IContext" />.</returns>
    IContext Create(PropagatedContext inbound);
}
