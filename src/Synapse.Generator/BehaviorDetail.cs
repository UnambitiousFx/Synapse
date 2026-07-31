namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     The C# "special" generic constraints on a behavior's type parameter that are not expressed as
///     constraint <em>types</em> (so they are invisible to <see cref="BehaviorDetail.RequestConstraints" />)
///     but as flags on the type-parameter symbol. Captured as an equatable value so the cross-product can
///     enforce them without carrying Roslyn symbols into the emit stage.
/// </summary>
[Flags]
public enum SpecialConstraints : byte
{
    None = 0,

    /// <summary><c>where T : class</c>.</summary>
    ReferenceType = 1,

    /// <summary><c>where T : struct</c>.</summary>
    ValueType = 2,

    /// <summary><c>where T : unmanaged</c> (also implies value type).</summary>
    Unmanaged = 4,

    /// <summary><c>where T : notnull</c>.</summary>
    NotNull = 8,

    /// <summary><c>where T : new()</c>.</summary>
    Constructor = 16
}

/// <summary>
///     Describes a class decorated with <c>[PipelineBehavior]</c> discovered by the source generator.
/// </summary>
public readonly record struct BehaviorDetail
{
    public BehaviorDetail(
        string className,
        string @namespace,
        string fullyQualifiedName,
        BehaviorKind kind,
        bool isOpenGeneric,
        string fullRequestTypeName,
        string? fullResponseOrItemTypeName,
        EquatableArray<string> requestConstraints,
        EquatableArray<string> responseConstraints,
        EquatableArray<int> closingTypeArgumentMap,
        SpecialConstraints requestSpecialConstraints = SpecialConstraints.None,
        SpecialConstraints responseSpecialConstraints = SpecialConstraints.None)
    {
        ClassName = className;
        Namespace = @namespace;
        FullyQualifiedName = fullyQualifiedName;
        Kind = kind;
        IsOpenGeneric = isOpenGeneric;
        FullRequestTypeName = fullRequestTypeName;
        FullResponseOrItemTypeName = fullResponseOrItemTypeName;
        RequestConstraints = requestConstraints;
        ResponseConstraints = responseConstraints;
        ClosingTypeArgumentMap = closingTypeArgumentMap;
        RequestSpecialConstraints = requestSpecialConstraints;
        ResponseSpecialConstraints = responseSpecialConstraints;
    }

    /// <summary>Simple class name (without namespace).</summary>
    public string ClassName { get; }

    /// <summary>Namespace of the behavior class.</summary>
    public string Namespace { get; }

    /// <summary>
    ///     Fully-qualified type name (including <c>global::</c> prefix and any enclosing types) of the behavior
    ///     class, with generic type parameters omitted. Used to emit a compilable registration even for nested
    ///     behavior classes; for open generics this is the base name the factory closes with type arguments.
    /// </summary>
    public string FullyQualifiedName { get; }

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

    /// <summary>
    ///     For an open-generic behavior: one entry per class type parameter (in class-declaration order),
    ///     holding the index of the implemented interface's type argument that binds it (<c>0</c> = request /
    ///     event slot, <c>1</c> = response / item slot), or <c>-1</c> when the parameter is bound by no
    ///     interface type argument and therefore cannot be inferred. Empty for closed behaviors. Drives the
    ///     order and arity of type arguments emitted when the class is closed over a matching handler.
    /// </summary>
    public EquatableArray<int> ClosingTypeArgumentMap { get; }

    /// <summary>
    ///     Special (non-type) constraints on the first (request/event) type parameter of an open-generic
    ///     behavior — <c>class</c>/<c>struct</c>/<c>unmanaged</c>/<c>notnull</c>/<c>new()</c>. A handler is
    ///     only cross-producted when its request type's shape satisfies these. <see cref="SpecialConstraints.None" />
    ///     for closed behaviors and unconstrained parameters.
    /// </summary>
    public SpecialConstraints RequestSpecialConstraints { get; }

    /// <summary>
    ///     Special (non-type) constraints on the second (response/item) type parameter of an open-generic
    ///     behavior. <see cref="SpecialConstraints.None" /> for closed behaviors and single-parameter behaviors.
    /// </summary>
    public SpecialConstraints ResponseSpecialConstraints { get; }

    /// <summary>
    ///     True when at least one class type parameter cannot be bound from the implemented pipeline interface
    ///     (a <c>-1</c> entry in <see cref="ClosingTypeArgumentMap" />), so the class cannot be closed safely.
    /// </summary>
    public bool HasUnbindableTypeParameter
    {
        get
        {
            foreach (var index in ClosingTypeArgumentMap)
            {
                if (index < 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public string FullBehaviorTypeName => FullyQualifiedName;
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

/// <summary>
///     Why a <c>[assembly: SynapseGlobalBehavior(typeof(...))]</c> entry could not be turned into a
///     registration, used to emit a precise diagnostic instead of letting generated code fail to compile.
/// </summary>
public enum GlobalBehaviorProblem
{
    /// <summary>The <c>typeof</c> argument did not resolve to a named type.</summary>
    NotAType,

    /// <summary>The type implements none of the known Synapse pipeline interfaces.</summary>
    NoPipelineInterface,

    /// <summary>The type is not <c>public</c>, so the generated registration could not reference it.</summary>
    Inaccessible
}

/// <summary>A rejected global-behavior entry: the offending type's display name and the reason.</summary>
public readonly record struct GlobalBehaviorDiagnostic
{
    public GlobalBehaviorDiagnostic(string typeName, GlobalBehaviorProblem problem)
    {
        TypeName = typeName;
        Problem = problem;
    }

    public string TypeName { get; }

    public GlobalBehaviorProblem Problem { get; }
}

/// <summary>
///     Result of scanning the assembly for <c>[assembly: SynapseGlobalBehavior(typeof(...))]</c>: the analyzed
///     behaviors to emit and any diagnostics for entries that could not be used. Fully materialized (no Roslyn
///     symbols) so it can flow through the incremental pipeline.
/// </summary>
public readonly record struct GlobalBehaviorInfo
{
    public GlobalBehaviorInfo(EquatableArray<BehaviorDetail> behaviors,
        EquatableArray<GlobalBehaviorDiagnostic> diagnostics)
    {
        Behaviors = behaviors;
        Diagnostics = diagnostics;
    }

    public EquatableArray<BehaviorDetail> Behaviors { get; }

    public EquatableArray<GlobalBehaviorDiagnostic> Diagnostics { get; }
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
