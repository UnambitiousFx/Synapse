namespace UnambitiousFx.Synapse.Abstractions.Exceptions;

/// <summary>
///     Exception thrown when a required context feature is not available.
/// </summary>
public sealed class MissingContextFeatureException : SynapseException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MissingContextFeatureException" /> class.
    /// </summary>
    /// <param name="featureType">The type of the missing feature.</param>
    public MissingContextFeatureException(Type featureType)
        : base($"The feature {featureType.Name} is not available in the current context.")
    {
    }
}