namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     The complete set of non-body inputs one endpoint reads, attached to the endpoint as a single
///     metadata entry.
/// </summary>
/// <remarks>
///     One wrapper rather than N loose <see cref="BoundParameterMetadata" /> entries: a single
///     <c>GetMetadata&lt;T&gt;()</c> retrieves the whole list, and it cannot be partially shadowed by
///     a group- or convention-level entry the way a repeated metadata type can.
/// </remarks>
public sealed class BoundParametersMetadata
{
    /// <summary>Initializes a new instance of the <see cref="BoundParametersMetadata" /> class.</summary>
    /// <param name="parameters">The parameters the endpoint's binder reads.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parameters" /> is <see langword="null" />.</exception>
    public BoundParametersMetadata(IReadOnlyList<BoundParameterMetadata> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        Parameters = parameters;
    }

    /// <summary>Gets the parameters, in the order the binder resolved them.</summary>
    public IReadOnlyList<BoundParameterMetadata> Parameters { get; }
}
