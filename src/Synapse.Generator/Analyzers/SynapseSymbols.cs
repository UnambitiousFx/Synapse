using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Generator.Analyzers;

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
}
