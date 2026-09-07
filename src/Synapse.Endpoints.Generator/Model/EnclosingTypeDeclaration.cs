using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>
///     One type that encloses an endpoint: enough to reopen it as a partial.
/// </summary>
/// <remarks>
///     The kind keyword travels with the name because generated code has to repeat it. Partial
///     declarations of one type must all use the same keyword (CS0261), so an endpoint nested inside
///     a <c>record</c> or a <c>struct</c> cannot be reopened with <c>partial class</c> — the shape
///     the top-level binder never had to think about, because it named the enclosing type rather
///     than reopening it. A <c>readonly record struct</c> with an explicit constructor for the same
///     reason as <see cref="EndpointDeclaration" />: this is cached state in the incremental
///     pipeline, and netstandard2.0 supplies no <c>IsExternalInit</c>.
/// </remarks>
internal readonly record struct EnclosingTypeDeclaration
{
    public EnclosingTypeDeclaration(string keyword,
        string name)
    {
        Keyword = keyword;
        Name = name;
    }

    /// <summary>The declaration keyword — <c>class</c>, <c>struct</c>, <c>record</c>, and so on.</summary>
    public string Keyword { get; }

    /// <summary>The type's own name, without namespace or enclosing types.</summary>
    public string Name { get; }

    /// <summary>Reads the keyword and name of one enclosing type.</summary>
    /// <param name="symbol">The enclosing type.</param>
    /// <returns>Its declaration.</returns>
    /// <remarks>
    ///     Generic enclosing types cannot reach here — SYNE010 rejects an endpoint nested inside one
    ///     — so the name never needs type parameters appended.
    /// </remarks>
    internal static EnclosingTypeDeclaration From(INamedTypeSymbol symbol)
    {
        var keyword = symbol.TypeKind switch
        {
            TypeKind.Struct => symbol.IsRecord ? "record struct" : "struct",
            TypeKind.Interface => "interface",
            _ => symbol.IsRecord ? "record" : "class"
        };

        return new EnclosingTypeDeclaration(keyword, symbol.Name);
    }
}
