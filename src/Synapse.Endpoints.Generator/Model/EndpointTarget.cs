namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>Which base class an endpoint derives from.</summary>
internal enum EndpointKind
{
    Void,
    Value,
    Mapped,
    Stream
}

/// <summary>
///     Equatable description of one discovered endpoint. Value equality matters: the incremental
///     pipeline caches on it, and a reference-typed member would defeat that.
/// </summary>
/// <remarks>
///     Declared with an explicit body rather than positional-record syntax: on netstandard2.0 the
///     compiler-generated <c>init</c> accessor a positional record relies on needs
///     <c>System.Runtime.CompilerServices.IsExternalInit</c>, which that target doesn't supply. The
///     sibling <c>Synapse.Generator</c> project sidesteps the same gap the same way throughout (see
///     <c>HandlerDetail</c>, <c>BehaviorDetail</c>, <c>RegisterGroupTarget</c>, <c>LocationInfo</c>);
///     this follows the same convention — a <c>record struct</c> for its value equality, get-only
///     properties assigned from an explicit constructor.
/// </remarks>
internal readonly record struct EndpointTarget
{
    public EndpointTarget(string endpointFullName,
        string boundTypeFullName,
        EndpointKind kind,
        string httpMethod,
        string route,
        string? groupFullName,
        LocationInfo? location,
        EquatableArray<BindablePropertyModel> boundProperties)
    {
        EndpointFullName = endpointFullName;
        BoundTypeFullName = boundTypeFullName;
        Kind = kind;
        HttpMethod = httpMethod;
        Route = route;
        GroupFullName = groupFullName;
        Location = location;
        BoundProperties = boundProperties;
    }

    /// <summary>Fully-qualified name of the endpoint class.</summary>
    public string EndpointFullName { get; }

    /// <summary>
    ///     Fully-qualified name of the type the binder is generated for: the message for
    ///     <see cref="EndpointKind.Void" />, <see cref="EndpointKind.Value" /> and
    ///     <see cref="EndpointKind.Stream" />; <c>THttpRequest</c> for <see cref="EndpointKind.Mapped" />.
    /// </summary>
    public string BoundTypeFullName { get; }

    /// <summary>Which base class the endpoint derives from.</summary>
    public EndpointKind Kind { get; }

    /// <summary>The HTTP method declared by the endpoint's attribute, or empty when declared in <c>Configure</c>.</summary>
    public string HttpMethod { get; }

    /// <summary>The route template declared by the endpoint's attribute, or empty when declared in <c>Configure</c>.</summary>
    public string Route { get; }

    /// <summary>Fully-qualified name of the group type declared by <c>InGroupAttribute</c>, if any.</summary>
    public string? GroupFullName { get; }

    /// <summary>Location of the endpoint class declaration, used to anchor diagnostics.</summary>
    public LocationInfo? Location { get; }

    /// <summary>
    ///     The bound type's properties that a binder should populate, in the order resolved from
    ///     the type's declaration. Two endpoints that bind the same type independently resolve this
    ///     the same way, since it depends only on the type's shape, its own route, and its own verb.
    /// </summary>
    public EquatableArray<BindablePropertyModel> BoundProperties { get; }
}
