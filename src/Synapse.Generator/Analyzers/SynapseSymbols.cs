using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

internal enum HandlerKind
{
    Request,
    RequestWithResponse,
    Event,
    Stream
}

internal readonly struct MessageInterface
{
    public MessageInterface(HandlerKind kind, ITypeSymbol messageType)
    {
        Kind = kind;
        MessageType = messageType;
    }

    public HandlerKind Kind { get; }

    public ITypeSymbol MessageType { get; }
}

/// <summary>
///     Symbol lookups shared by the Synapse analyzers. Everything is matched by name in the Abstractions
///     namespace so the analyzers work against any version of the package.
/// </summary>
internal static class SynapseSymbols
{
    public const string AbstractionsNamespace = "UnambitiousFx.Synapse.Abstractions";
    public const string AbstractionsAssemblyName = "UnambitiousFx.Synapse.Abstractions";

    public static bool ReferencesSynapse(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName($"{AbstractionsNamespace}.IRequest") is not null;
    }

    public static bool IsInAbstractions(ISymbol symbol)
    {
        return symbol.ContainingNamespace?.ToDisplayString() == AbstractionsNamespace;
    }

    public static bool IsAttribute(INamedTypeSymbol? attributeClass, string attributeName)
    {
        return attributeClass is not null && attributeClass.Name == attributeName && IsInAbstractions(attributeClass);
    }

    public static bool IsHandlerAttribute(INamedTypeSymbol? attributeClass)
    {
        return IsAttribute(attributeClass, "RequestHandlerAttribute")
               || IsAttribute(attributeClass, "EventHandlerAttribute")
               || IsAttribute(attributeClass, "StreamRequestHandlerAttribute");
    }

    public static bool HasHandlerAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attribute => IsHandlerAttribute(attribute.AttributeClass));
    }

    public static MessageInterface? GetHandlerInterface(INamedTypeSymbol iface)
    {
        if (!IsInAbstractions(iface))
        {
            return null;
        }

        return iface.MetadataName switch
        {
            "IRequestHandler`1" => new MessageInterface(HandlerKind.Request, iface.TypeArguments[0]),
            "IRequestHandler`2" => new MessageInterface(HandlerKind.RequestWithResponse, iface.TypeArguments[0]),
            "IEventHandler`1" => new MessageInterface(HandlerKind.Event, iface.TypeArguments[0]),
            "IStreamRequestHandler`2" => new MessageInterface(HandlerKind.Stream, iface.TypeArguments[0]),
            _ => null
        };
    }

    public static bool IsRequestInterface(INamedTypeSymbol iface)
    {
        return IsInAbstractions(iface) && iface.MetadataName is "IRequest" or "IRequest`1";
    }
}
