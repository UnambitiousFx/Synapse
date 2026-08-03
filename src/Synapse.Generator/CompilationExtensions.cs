using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Generator;

internal static class CompilationExtensions
{
    public static string GetRootNamespaceFromAssemblyAttributes(this Compilation compilation)
    {
        // Fallback used only when the MSBuild RootNamespace (build_property.RootNamespace) is unavailable.
        // AssemblyDefaultAliasAttribute is not populated from RootNamespace, so this is a best-effort guess
        // that ultimately degrades to the assembly name — which can differ from the intended root namespace.
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