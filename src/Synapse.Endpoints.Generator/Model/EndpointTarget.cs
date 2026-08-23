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
        EquatableArray<BindablePropertyModel> boundProperties,
        bool hasParameterlessConstructor,
        EquatableArray<string> primaryConstructorParameterNames)
    {
        EndpointFullName = endpointFullName;
        BoundTypeFullName = boundTypeFullName;
        Kind = kind;
        HttpMethod = httpMethod;
        Route = route;
        GroupFullName = groupFullName;
        Location = location;
        BoundProperties = boundProperties;
        HasParameterlessConstructor = hasParameterlessConstructor;
        PrimaryConstructorParameterNames = primaryConstructorParameterNames;
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
    ///     The bound type's properties that a binder should populate, resolved from this endpoint's
    ///     own route and verb.
    /// </summary>
    /// <remarks>
    ///     Resolution depends on the endpoint's own route template and HTTP verb (rules 3 and 4 of
    ///     the binding-source table), not on the type alone. When two endpoints share a bound type
    ///     with different routes or verbs, only one binder is emitted for that type — keyed by the
    ///     type, per <c>EndpointRegistry.RegisterBinder</c>'s design — and it is built from whichever
    ///     endpoint sorts first by <see cref="EndpointFullName" />. The other endpoint silently binds
    ///     using that resolution instead of its own; this is not currently detected. See
    ///     <c>BinderEmissionEdgeCaseTests.Generate_ForTypeSharedByEndpointsWithDifferentVerbs_...</c>
    ///     for a test that pins today's behaviour, and Task 17's planned SYNE013 for the diagnostic
    ///     that will report the conflict instead of silently picking a winner.
    /// </remarks>
    public EquatableArray<BindablePropertyModel> BoundProperties { get; }

    /// <summary>
    ///     Whether the bound type has an accessible parameterless constructor. When false (a
    ///     positional record, or a hand-written type with only a parameterized constructor), a
    ///     bodyless binder must construct through <see cref="PrimaryConstructorParameterNames" />
    ///     instead of <c>new T()</c>, which would not compile.
    /// </summary>
    public bool HasParameterlessConstructor { get; }

    /// <summary>
    ///     The parameter names (in order) of the accessible constructor with the most parameters,
    ///     used to construct the bound type when <see cref="HasParameterlessConstructor" /> is
    ///     false. Empty when a parameterless constructor exists. Each name is matched
    ///     case-insensitively against <see cref="BoundProperties" /> at emit time; a parameter with
    ///     no matching property (or whose matching property was itself omitted, e.g. for having no
    ///     viable <c>TryParse</c>) is passed <c>default</c>.
    /// </summary>
    public EquatableArray<string> PrimaryConstructorParameterNames { get; }
}
