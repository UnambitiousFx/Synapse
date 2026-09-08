namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>Which base class an endpoint derives from.</summary>
internal enum EndpointKind
{
    /// <summary><c>Endpoint&lt;TRequest&gt;</c> — generated binder, no response body.</summary>
    Void,

    /// <summary><c>Endpoint&lt;TRequest, TResponse&gt;</c> — generated binder, response body.</summary>
    Value,

    /// <summary><c>ContractEndpoint&lt;...&gt;</c> — generated binder over the wire DTO.</summary>
    Contract,

    /// <summary><c>StreamEndpoint&lt;TRequest, TItem&gt;</c> — generated binder, streamed body.</summary>
    Stream,

    /// <summary><c>InlineEndpoint&lt;TRequest&gt;</c> — generated binder, no dispatch, no response body.</summary>
    InlineVoid,

    /// <summary><c>InlineEndpoint&lt;TRequest, TResponse&gt;</c> — generated binder, no dispatch, response body.</summary>
    Inline,

    /// <summary><c>BoundEndpoint</c> — the free-form low level; binds nothing, returns its own result.</summary>
    Raw,

    /// <summary><c>BoundEndpoint&lt;TRequest&gt;</c> — hand-written binding, no response body.</summary>
    BoundVoid,

    /// <summary><c>BoundEndpoint&lt;TRequest, TResponse&gt;</c> — hand-written binding, response body.</summary>
    BoundValue
}

/// <summary>Which generator behaviours apply to each endpoint kind.</summary>
internal static class EndpointKindExtensions
{
    /// <summary>
    ///     Whether the analyzer generates a binding for this kind, and therefore whether the
    ///     binding diagnostics (SYNE001, SYNE002, SYNE007, SYNE011, SYNE012, SYNE014, SYNE015) have
    ///     anything to say about it.
    /// </summary>
    /// <remarks>
    ///     The low level exists precisely so that binding can be written by hand:
    ///     <see cref="EndpointKind.Raw" /> binds nothing at all, and the two
    ///     <c>BoundEndpoint&lt;…&gt;</c> kinds supply their own <c>BindAsync</c>. Reporting a binding
    ///     diagnostic against code the generator did not write would be a false positive every time.
    /// </remarks>
    internal static bool HasGeneratedBinder(this EndpointKind kind)
    {
        return kind is EndpointKind.Void or EndpointKind.Value or EndpointKind.Contract or EndpointKind.Stream
            or EndpointKind.Inline or EndpointKind.InlineVoid;
    }

    /// <summary>
    ///     Whether the base class knows the type of the message this endpoint dispatches, so the
    ///     diagnostics about dispatch and success mapping (SYNE003, SYNE005) apply.
    /// </summary>
    internal static bool DispatchesKnownMessage(this EndpointKind kind)
    {
        return kind is EndpointKind.Void or EndpointKind.Value or EndpointKind.BoundVoid or EndpointKind.BoundValue;
    }

    /// <summary>Whether this kind returns a single response value, so SYNE003 applies.</summary>
    /// <remarks>
    ///     <see cref="EndpointKind.Inline" /> included: it produces the response itself rather
    ///     than dispatching for it, but it maps that response through the same <c>OnSuccess</c> and
    ///     declarative builder methods, so the nudge means the same thing there.
    /// </remarks>
    internal static bool ReturnsValue(this EndpointKind kind)
    {
        return kind is EndpointKind.Value or EndpointKind.BoundValue or EndpointKind.Inline;
    }
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
        EquatableArray<ConstructorParameterModel> primaryConstructorParameters,
        string? jsonRequestTypeName,
        string? jsonResponseTypeName,
        EquatableArray<JsonCallSite> jsonCallSites,
        EndpointDeclaration declaration)
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
        PrimaryConstructorParameters = primaryConstructorParameters;
        JsonRequestTypeName = jsonRequestTypeName;
        JsonResponseTypeName = jsonResponseTypeName;
        JsonCallSites = jsonCallSites;
        Declaration = declaration;
    }

    /// <summary>Fully-qualified name of the endpoint class.</summary>
    public string EndpointFullName { get; }

    /// <summary>
    ///     Fully-qualified name of the type the binder is generated for: the message for
    ///     <see cref="EndpointKind.Void" />, <see cref="EndpointKind.Value" /> and
    ///     <see cref="EndpointKind.Stream" />; <c>THttpRequest</c> for <see cref="EndpointKind.Contract" />;
    ///     the request contract — which is not a message at all — for the two self-handled kinds.
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
    ///     the binding-source table), not on the type alone — which is why the binding is emitted per
    ///     endpoint, into the endpoint's own partial class, rather than once per bound type. Two
    ///     endpoints sharing one message therefore each bind by their own resolution; nothing is keyed
    ///     by bound type any more.
    /// </remarks>
    public EquatableArray<BindablePropertyModel> BoundProperties { get; }

    /// <summary>
    ///     Whether the bound type has an accessible parameterless constructor. When false (a
    ///     positional record, or a hand-written type with only a parameterized constructor), a
    ///     bodyless binder must construct through <see cref="PrimaryConstructorParameters" />
    ///     instead of <c>new T()</c>, which would not compile.
    /// </summary>
    public bool HasParameterlessConstructor { get; }

    /// <summary>
    ///     The parameters (in order) of the accessible constructor with the most parameters, used to
    ///     construct the bound type when <see cref="HasParameterlessConstructor" /> is false. Empty
    ///     when a parameterless constructor exists. Each parameter's name is matched
    ///     case-insensitively against <see cref="BoundProperties" /> at emit time; a parameter with
    ///     no matching property (or whose matching property was itself omitted, e.g. for having no
    ///     viable <c>TryParse</c>) is passed a literal default value — <c>default!</c> for a
    ///     reference-typed parameter, so the generated code does not raise a nullable-reference
    ///     warning, and bare <c>default</c> for a value-typed one.
    /// </summary>
    public EquatableArray<ConstructorParameterModel> PrimaryConstructorParameters { get; }

    /// <summary>
    ///     SYNE008: the display name of the request/bound type, when it is actually deserialized
    ///     from the JSON request body (a non-bodyless verb, or a property explicitly bound via
    ///     <c>[FromBody]</c>) — otherwise null, since a type never reaching the JSON deserializer has
    ///     nothing to register. Excludes primitives and well-known framework scalar types (see
    ///     <see cref="Diagnostics.EndpointDiagnostics.MissingJsonSerializableRegistration" />), which
    ///     are also represented as null here.
    /// </summary>
    public string? JsonRequestTypeName { get; }

    /// <summary>
    ///     SYNE008: the display name of the type written back as the response body — <c>TResponse</c>
    ///     for <see cref="EndpointKind.Value" /> and <see cref="EndpointKind.Inline" />,
    ///     <c>THttpResponse</c> for <see cref="EndpointKind.Contract" />, <c>TItem</c> for
    ///     <see cref="EndpointKind.Stream" /> — or null for <see cref="EndpointKind.Void" /> and
    ///     <see cref="EndpointKind.InlineVoid" /> (no response body at all) and for a
    ///     primitive/framework scalar type, which needs no registration.
    /// </summary>
    public string? JsonResponseTypeName { get; }

    /// <summary>
    ///     SYNE008: the types this endpoint names at its own call sites — a <c>BodyAsync&lt;T&gt;</c>
    ///     read, or an <c>Accepts&lt;T&gt;</c>/<c>Produces&lt;T&gt;</c> declaration. Populated only for
    ///     the low-level kinds, whose JSON-relevant types cannot be read off a base class.
    /// </summary>
    public EquatableArray<JsonCallSite> JsonCallSites { get; }

    /// <summary>How the endpoint class is declared, for reopening it as a partial.</summary>
    public EndpointDeclaration Declaration { get; }
}
