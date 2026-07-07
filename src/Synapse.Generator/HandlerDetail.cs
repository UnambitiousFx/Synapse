namespace UnambitiousFx.Synapse.Generator;

/// <summary>
///     The shape of a request/event/response type, as the equatable facts needed to evaluate an
///     open-generic behavior's special constraints (<c>class</c>/<c>struct</c>/<c>unmanaged</c>/
///     <c>notnull</c>/<c>new()</c>) without carrying Roslyn symbols into the emit stage.
/// </summary>
public readonly record struct TypeShape
{
    public TypeShape(bool isReferenceType,
        bool isValueType,
        bool isUnmanaged,
        bool isNotNull,
        bool hasParameterlessCtor)
    {
        IsReferenceType = isReferenceType;
        IsValueType = isValueType;
        IsUnmanaged = isUnmanaged;
        IsNotNull = isNotNull;
        HasParameterlessCtor = hasParameterlessCtor;
    }

    public bool IsReferenceType { get; }
    public bool IsValueType { get; }
    public bool IsUnmanaged { get; }
    public bool IsNotNull { get; }
    public bool HasParameterlessCtor { get; }
}

public readonly record struct HandlerDetail
{
    public HandlerDetail(HandlerType handlerType,
        string className,
        string @namespace,
        string fullyQualifiedName,
        string fullTargetTypeName,
        string? fullResponseType,
        LocationInfo? location,
        EquatableArray<string> targetSatisfyingTypes,
        EquatableArray<string> responseSatisfyingTypes,
        TypeShape requestShape = default,
        TypeShape responseShape = default)
    {
        HandlerType = handlerType;
        ClassName = className;
        Namespace = @namespace;
        FullyQualifiedName = fullyQualifiedName;
        FullTargetTypeName = fullTargetTypeName;
        FullResponseType = fullResponseType;
        Location = location;
        TargetSatisfyingTypes = targetSatisfyingTypes;
        ResponseSatisfyingTypes = responseSatisfyingTypes;
        RequestShape = requestShape;
        ResponseShape = responseShape;
    }

    public string ClassName { get; }
    public string Namespace { get; }

    /// <summary>
    ///     Fully-qualified type name (including <c>global::</c> prefix and any enclosing types) of the handler
    ///     class, with generic type parameters omitted. Used to emit a compilable registration even for nested
    ///     handler classes.
    /// </summary>
    public string FullyQualifiedName { get; }

    public string FullTargetTypeName { get; }
    public string? FullResponseType { get; }
    public LocationInfo? Location { get; }
    public HandlerType HandlerType { get; }

    /// <summary>
    ///     The request/event type plus all of its base types and implemented interfaces, as fully-qualified
    ///     display strings. Used to decide whether the request type satisfies an open-generic behavior's
    ///     named-type constraints.
    /// </summary>
    public EquatableArray<string> TargetSatisfyingTypes { get; }

    /// <summary>
    ///     The response / item type plus all of its base types and implemented interfaces, as fully-qualified
    ///     display strings. Empty when the handler has no response.
    /// </summary>
    public EquatableArray<string> ResponseSatisfyingTypes { get; }

    /// <summary>The request/event type's shape, used to evaluate behavior special constraints.</summary>
    public TypeShape RequestShape { get; }

    /// <summary>The response/item type's shape. <c>default</c> when the handler has no response.</summary>
    public TypeShape ResponseShape { get; }

    public string FullHandlerTypeName => FullyQualifiedName;
}
