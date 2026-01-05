namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a feature that can be associated with a context.
/// </summary>
public interface IContextFeature
{
    /// <summary>
    ///     Gets the name of the context feature.
    /// </summary>
    string Name { get; }
}