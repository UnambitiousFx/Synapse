namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>
///     One type enclosing an endpoint that is not declared <c>partial</c>, so cannot be reopened by
///     generated code. SYNE020 reports each of them.
/// </summary>
/// <remarks>
///     The location travels with the name because the diagnostic names this type and must therefore
///     be reported on it: anchored at the endpoint instead, the squiggle sat on a different type than
///     the one the author has to change. A <c>readonly record struct</c> with an explicit
///     constructor for the same reason as <see cref="EndpointDeclaration" /> — this is cached state
///     in the incremental pipeline, and netstandard2.0 supplies no <c>IsExternalInit</c>.
/// </remarks>
internal readonly record struct NonPartialEnclosingType
{
    public NonPartialEnclosingType(string name,
        LocationInfo? location)
    {
        Name = name;
        Location = location;
    }

    /// <summary>The type's own name, without namespace or enclosing types.</summary>
    public string Name { get; }

    /// <summary>Where the type is declared, or null when it has no source location.</summary>
    public LocationInfo? Location { get; }
}
