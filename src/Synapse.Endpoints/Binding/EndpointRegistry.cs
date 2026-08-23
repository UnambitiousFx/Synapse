namespace UnambitiousFx.Synapse.Endpoints.Binding;

/// <summary>
///     Receives the binder and route metadata that generated code produces for each endpoint.
///     Populated from a module initializer, so it is ready before any endpoint is mapped.
/// </summary>
public static class EndpointRegistry
{
    /// <summary>Registers the binder for a message type.</summary>
    /// <typeparam name="TRequest">The message type.</typeparam>
    /// <param name="binder">The generated binder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="binder" /> is <see langword="null" />.</exception>
    public static void RegisterBinder<TRequest>(IEndpointBinder<TRequest> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        BinderHolder<TRequest>.Instance = binder;
    }

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

    /// <summary>Gets the binder for a message type.</summary>
    /// <typeparam name="TRequest">The message type.</typeparam>
    /// <returns>The registered binder.</returns>
    /// <exception cref="InvalidOperationException">No binder was registered.</exception>
    public static IEndpointBinder<TRequest> GetBinder<TRequest>()
    {
        return BinderHolder<TRequest>.Instance
               ?? throw new InvalidOperationException(
                   $"No binder was registered for '{typeof(TRequest).Name}'. The Synapse.Endpoints " +
                   "analyzer generates binders at compile time; verify it is enabled for the assembly " +
                   "declaring this endpoint and that analyzers are not disabled for the build.");
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

    private static class BinderHolder<TRequest>
    {
        internal static IEndpointBinder<TRequest>? Instance;
    }

    private static class MetadataHolder<TEndpoint>
        where TEndpoint : EndpointBase
    {
        internal static EndpointMetadata? Instance;
    }
}
