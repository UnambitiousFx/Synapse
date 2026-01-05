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
            return rootNamespace;

        // Alternative: try the assembly name as fallback.
        return compilation.AssemblyName ?? string.Empty;
    }
}