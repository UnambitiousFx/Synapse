namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     Describes a class decorated with <c>[PipelineBehavior]</c> discovered by the source generator.
/// </summary>
public readonly record struct BehaviorDetail
{
    public BehaviorDetail(
        string className,
        string @namespace,
        BehaviorKind kind,
        bool isOpenGeneric,
        string fullRequestTypeName,
        string? fullResponseOrItemTypeName,
        int order,
        EquatableArray<string> requestConstraints,
        EquatableArray<string> responseConstraints)
    {
        ClassName = className;
        Namespace = @namespace;
        Kind = kind;
        IsOpenGeneric = isOpenGeneric;
        FullRequestTypeName = fullRequestTypeName;
        FullResponseOrItemTypeName = fullResponseOrItemTypeName;
        Order = order;
        RequestConstraints = requestConstraints;
        ResponseConstraints = responseConstraints;
    }

    /// <summary>Simple class name (without namespace).</summary>
    public string ClassName { get; }

    /// <summary>Namespace of the behavior class.</summary>
    public string Namespace { get; }

    /// <summary>Whether this is an open-generic class (cross-product with all matching handlers).</summary>
    public bool IsOpenGeneric { get; }

    /// <summary>Pipeline kind derived from the interface the class implements.</summary>
    public BehaviorKind Kind { get; }

    /// <summary>
    ///     For a closed behavior: the fully-qualified request/event type.
    ///     For an open-generic behavior: the name of the first type parameter (used for constraint checking).
    /// </summary>
    public string FullRequestTypeName { get; }

    /// <summary>
    ///     For closed behaviors with a response / item type (request-with-response, stream): the fully-qualified type.
    ///     Null for no-response request and event behaviors.
    ///     For open-generic behaviors: name of the second type parameter.
    /// </summary>
    public string? FullResponseOrItemTypeName { get; }

    /// <summary>Controls pipeline ordering; lower runs first (outermost).</summary>
    public int Order { get; }

    /// <summary>
    ///     Named-type constraints on the first (request) type parameter of an open-generic behavior, as
    ///     fully-qualified display strings. A handler is only cross-producted with this behavior when its
    ///     request type satisfies all of these. Empty for closed behaviors.
    /// </summary>
    public EquatableArray<string> RequestConstraints { get; }

    /// <summary>
    ///     Named-type constraints on the second (response / item) type parameter of an open-generic behavior.
    ///     Empty for closed behaviors and for single-parameter behaviors.
    /// </summary>
    public EquatableArray<string> ResponseConstraints { get; }

    public string FullBehaviorTypeName => $"{Namespace}.{ClassName}";
}

/// <summary>
///     Result of scanning a single <c>[PipelineBehavior]</c>-decorated class. A class may implement more than
///     one pipeline interface, so it can yield multiple <see cref="BehaviorDetail" />s. An empty
///     <see cref="Behaviors" /> collection means the class implements no known pipeline interface (MDG008).
/// </summary>
public readonly record struct BehaviorScan
{
    public BehaviorScan(LocationInfo? location, EquatableArray<BehaviorDetail> behaviors)
    {
        Location = location;
        Behaviors = behaviors;
    }

    public LocationInfo? Location { get; }

    public EquatableArray<BehaviorDetail> Behaviors { get; }
}

/// <summary>The pipeline interface kind a behavior implements.</summary>
public enum BehaviorKind
{
    /// <summary><c>IRequestPipelineBehavior&lt;TRequest&gt;</c> (no response).</summary>
    Request,

    /// <summary><c>IRequestPipelineBehavior&lt;TRequest, TResponse&gt;</c> (with response).</summary>
    RequestWithResponse,

    /// <summary><c>IEventPipelineBehavior&lt;TEvent&gt;</c>.</summary>
    Event,

    /// <summary><c>IStreamRequestPipelineBehavior&lt;TRequest, TItem&gt;</c>.</summary>
    StreamRequest
}
