namespace UnambitiousFx.Synapse.Generator;

public readonly record struct HandlerDetail
{
    public HandlerDetail(HandlerType handlerType,
        string className,
        string @namespace,
        string fullTargetTypeName,
        string? fullResponseType,
        LocationInfo? location,
        EquatableArray<string> targetSatisfyingTypes,
        EquatableArray<string> responseSatisfyingTypes)
    {
        HandlerType = handlerType;
        ClassName = className;
        Namespace = @namespace;
        FullTargetTypeName = fullTargetTypeName;
        FullResponseType = fullResponseType;
        Location = location;
        TargetSatisfyingTypes = targetSatisfyingTypes;
        ResponseSatisfyingTypes = responseSatisfyingTypes;
    }

    public string ClassName { get; }
    public string Namespace { get; }
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

    public string FullHandlerTypeName => $"{Namespace}.{ClassName}";
}
