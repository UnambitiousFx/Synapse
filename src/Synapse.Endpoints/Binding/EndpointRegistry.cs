namespace UnambitiousFx.Synapse.Endpoints.Binding;

/// <summary>
///     Receives the route metadata that generated code produces for each endpoint. Populated from a
///     module initializer, so it is ready before any endpoint is mapped.
/// </summary>
/// <remarks>
///     Binders used to live here too, keyed by message type. They do not any more: every generated
///     tier's binding is emitted into the endpoint's own <c>partial</c> as an <c>override</c> the
///     compiler requires, so there is nothing to look up and nothing that can be missing.
/// </remarks>
public static class EndpointRegistry
{
    /// <summary>Registers the route metadata for an endpoint type.</summary>
    /// <typeparam name="TEndpoint">The endpoint type.</typeparam>
    /// <param name="metadata">The metadata read from the endpoint's attributes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="metadata" /> is <see langword="null" />.</exception>
    public static void RegisterMetadata<TEndpoint>(EndpointMetadata metadata)
        where TEndpoint : EndpointBase
    {
        ArgumentNullException.ThrowIfNull(metadata);
        MetadataHolder<TEndpoint>.Instance = metadata;
    }

    /// <summary>Gets the route metadata for an endpoint type.</summary>
    /// <typeparam name="TEndpoint">The endpoint type.</typeparam>
    /// <returns>The registered metadata.</returns>
    /// <exception cref="InvalidOperationException">No metadata was registered.</exception>
    public static EndpointMetadata GetMetadata<TEndpoint>()
        where TEndpoint : EndpointBase
    {
        return MetadataHolder<TEndpoint>.Instance
               ?? throw new InvalidOperationException(
                   $"No route metadata was registered for endpoint '{typeof(TEndpoint).Name}'. The " +
                   "Synapse.Endpoints analyzer generates this registration at compile time; verify it " +
                   "is enabled for the assembly declaring this endpoint.");
    }

    /// <summary>Gets the route metadata for an endpoint type, without throwing when none was registered.</summary>
    /// <typeparam name="TEndpoint">The endpoint type.</typeparam>
    /// <returns>The registered metadata, or <see langword="null" /> when none was registered.</returns>
    internal static EndpointMetadata? TryGetMetadata<TEndpoint>()
        where TEndpoint : EndpointBase
    {
        return MetadataHolder<TEndpoint>.Instance;
    }

    private static class MetadataHolder<TEndpoint>
        where TEndpoint : EndpointBase
    {
        internal static EndpointMetadata? Instance;
    }
}
