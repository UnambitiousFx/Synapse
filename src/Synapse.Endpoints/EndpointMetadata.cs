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
    }

    /// <summary>Gets the HTTP methods declared by the endpoint's attribute.</summary>
    public string[] HttpMethods { get; }

    /// <summary>Gets the route template declared by the endpoint's attribute.</summary>
    public string Route { get; }

    /// <summary>Gets the group type declared by <c>InGroupAttribute</c>, if any.</summary>
    public Type? GroupType { get; }

    /// <summary>
    ///     Gets a value indicating whether the route was left to <c>Configure</c> to declare.
    /// </summary>
    public bool IsRouteDeclaredInConfigure => Route.Length == 0;
}
