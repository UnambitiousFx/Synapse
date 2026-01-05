namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Provides access to the current <see cref="IContext" />.
/// </summary>
public interface IContextAccessor
{
    /// <summary>
    ///     Gets or sets the current context.
    /// </summary>
    IContext Context { get; set; }
}