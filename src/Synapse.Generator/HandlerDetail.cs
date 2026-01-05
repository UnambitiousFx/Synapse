using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Generator;

public readonly record struct HandlerDetail
{
    public HandlerDetail(HandlerType handlerType,
        string className,
        string @namespace,
        string fullTargetTypeName,
        string? fullResponseType,
        Location? location)
    {
        HandlerType = handlerType;
        ClassName = className;
        Namespace = @namespace;
        FullTargetTypeName = fullTargetTypeName;
        FullResponseType = fullResponseType;
        Location = location;
    }

    public string ClassName { get; }
    public string Namespace { get; }
    public string FullTargetTypeName { get; }
    public string? FullResponseType { get; }
    public Location? Location { get; }
    public HandlerType HandlerType { get; }
    public string FullHandlerTypeName => $"{Namespace}.{ClassName}";
}