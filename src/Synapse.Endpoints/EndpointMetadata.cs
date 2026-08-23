namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     Route information for one endpoint, supplied by generated registration code so that
///     nothing has to read attributes reflectively at runtime.
/// </summary>
public sealed class EndpointMetadata
{
    /// <summary>Initializes a new instance of the <see cref="EndpointMetadata" /> class.</summary>
    /// <param name="httpMethods">The HTTP methods, or empty when <c>Configure</c> declares them.</param>
    /// <param name="route">The route template, or empty when <c>Configure</c> declares it.</param>
    /// <param name="groupType">The group this endpoint belongs to, if any.</param>
    public EndpointMetadata(string[] httpMethods,
        string route,
        Type? groupType = null)
    {
        HttpMethods = httpMethods;
        Route = route;
        GroupType = groupType;
        GroupFactory = null;
    }

    /// <summary>Initializes a new instance of the <see cref="EndpointMetadata" /> class for an endpoint that belongs to a group.</summary>
    /// <param name="httpMethods">The HTTP methods, or empty when <c>Configure</c> declares them.</param>
    /// <param name="route">The route template, or empty when <c>Configure</c> declares it.</param>
    /// <param name="groupType">The group this endpoint belongs to.</param>
    /// <param name="groupFactory">
    ///     Creates the group instance. No reflection is used to instantiate groups, so a factory must
    ///     be supplied whenever <paramref name="groupType" /> is set; the Synapse.Endpoints analyzer
    ///     emits it alongside the route metadata as <c>static () =&gt; new TGroup()</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="groupType" /> or <paramref name="groupFactory" /> is <see langword="null" />.
    /// </exception>
    public EndpointMetadata(string[] httpMethods,
        string route,
        Type groupType,
        Func<EndpointGroup> groupFactory)
    {
        ArgumentNullException.ThrowIfNull(groupType);
        ArgumentNullException.ThrowIfNull(groupFactory);

        HttpMethods = httpMethods;
        Route = route;
        GroupType = groupType;
        GroupFactory = groupFactory;
    }

    /// <summary>Gets the HTTP methods declared by the endpoint's attribute.</summary>
    public string[] HttpMethods { get; }

    /// <summary>Gets the route template declared by the endpoint's attribute.</summary>
    public string Route { get; }

    /// <summary>Gets the group type declared by <c>InGroupAttribute</c>, if any.</summary>
    public Type? GroupType { get; }

    /// <summary>
    ///     Gets the factory that creates the <see cref="GroupType" /> instance, if a group was
    ///     declared. No reflection is used to instantiate groups, so this is required whenever
    ///     <see cref="GroupType" /> is set; <see cref="EndpointRouteBuilderExtensions.MapEndpoint{TEndpoint}" />
    ///     throws when it is missing.
    /// </summary>
    public Func<EndpointGroup>? GroupFactory { get; }

    /// <summary>
    ///     Gets a value indicating whether the route was left to <c>Configure</c> to declare.
    /// </summary>
    public bool IsRouteDeclaredInConfigure => Route.Length == 0;
}
