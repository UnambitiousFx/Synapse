using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Endpoints.Generator;

/// <summary>
///     Compilation-level lookups the generator needs but Roslyn does not surface directly.
/// </summary>
internal static class CompilationExtensions
{
    /// <summary>
    ///     Best-effort root namespace derived from the assembly itself, for use when the MSBuild
    ///     <c>RootNamespace</c> property is unavailable — either genuinely absent, or <em>present but
    ///     empty</em>, which is what a project declaring <c>&lt;RootNamespace&gt;&lt;/RootNamespace&gt;</c>
    ///     surfaces to a generator and what a plain <c>??</c> never catches.
    /// </summary>
    /// <param name="compilation">The compilation under generation.</param>
    /// <returns>The assembly's declared default alias, or its name, or the empty string.</returns>
    /// <remarks>
    ///     Mirrors <c>Synapse.Generator</c>'s identically named extension, deliberately: the two
    ///     generators emit into consumer assemblies the same way and should answer "what namespace do
    ///     I emit into?" the same way. Duplicated rather than shared because neither generator project
    ///     references the other. <c>AssemblyDefaultAliasAttribute</c> is not actually populated from
    ///     <c>RootNamespace</c> by the SDK, so in practice this degrades to the assembly name — which
    ///     is a namespace a consumer would plausibly have typed, unlike a hardcoded fallback.
    /// </remarks>
    internal static string GetRootNamespaceFromAssemblyAttributes(this Compilation compilation)
    {
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

        return compilation.AssemblyName ?? string.Empty;
    }
}
