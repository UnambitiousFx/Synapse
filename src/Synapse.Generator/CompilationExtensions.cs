using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Generator;

internal static class CompilationExtensions
{
    public static string GetRootNamespaceFromAssemblyAttributes(this Compilation compilation)
    {
        // Look for the RootNamespace assembly attribute
        var rootNamespaceAttribute = compilation.Assembly.GetAttributes()
            .FirstOrDefault(attr =>
                attr.AttributeClass?.Name == "AssemblyDefaultAliasAttribute" ||
                attr.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyDefaultAliasAttribute");

        if (rootNamespaceAttribute != null &&
            rootNamespaceAttribute.ConstructorArguments.Length > 0 &&
            rootNamespaceAttribute.ConstructorArguments[0].Value is string rootNamespace)
        {
            return rootNamespace;
        }

        // Alternative: try the assembly name as fallback.
        return compilation.AssemblyName ?? string.Empty;
    }

    /// <summary>
    ///     Returns true when the assembly is decorated with
    ///     <c>[assembly: EnableSynapseCqrsBoundaryEnforcement]</c>, opting into generator-emitted
    ///     closed CQRS boundary enforcement registrations.
    /// </summary>
    public static bool IsCqrsBoundaryEnforcementEnabled(this Compilation compilation)
    {
        return compilation.Assembly.GetAttributes()
            .Any(attr =>
                attr.AttributeClass?.Name == "EnableSynapseCqrsBoundaryEnforcementAttribute" ||
                attr.AttributeClass?.ToDisplayString() ==
                "UnambitiousFx.Synapse.Abstractions.EnableSynapseCqrsBoundaryEnforcementAttribute");
    }

    /// <summary>
    ///     Returns true when the assembly is decorated with
    ///     <c>[assembly: DisableSynapseCrossAssemblyBehaviors]</c>, opting out of applying the assembly's
    ///     open-generic pipeline behaviors to handlers declared in referenced assemblies. When set, behavior
    ///     cross-product is restricted to handlers declared in this assembly.
    /// </summary>
    public static bool IsCrossAssemblyBehaviorsDisabled(this Compilation compilation)
    {
        return compilation.Assembly.GetAttributes()
            .Any(attr =>
                attr.AttributeClass?.Name == "DisableSynapseCrossAssemblyBehaviorsAttribute" ||
                attr.AttributeClass?.ToDisplayString() ==
                "UnambitiousFx.Synapse.Abstractions.DisableSynapseCrossAssemblyBehaviorsAttribute");
    }
}