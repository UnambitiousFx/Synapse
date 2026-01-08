namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
/// Provides a mechanism to set the current <see cref="IContext" />.
/// </summary>
public interface IContextSetter
{
    /// <summary>
    ///     Gets or sets the current context.
    /// </summary>
    IContext Context { get; set; }
}