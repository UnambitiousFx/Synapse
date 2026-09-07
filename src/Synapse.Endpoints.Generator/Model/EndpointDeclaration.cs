namespace UnambitiousFx.Synapse.Endpoints.Generator.Model;

/// <summary>
///     How one endpoint is declared: enough to reopen its class as a partial in generated code.
/// </summary>
/// <remarks>
///     A <c>readonly record struct</c> with an explicit constructor, and <c>EquatableArray</c> for
///     both collections, because this is cached state in the incremental pipeline — see
///     <see cref="EndpointTarget" />'s remarks for why positional syntax is unavailable on
///     netstandard2.0.
/// </remarks>
internal readonly record struct EndpointDeclaration
{
    public EndpointDeclaration(string @namespace,
        string typeName,
        bool isPartial,
        bool isSealed,
        EquatableArray<EnclosingTypeDeclaration> enclosingTypes,
        EquatableArray<string> nonPartialEnclosingTypeNames)
    {
        Namespace = @namespace;
        TypeName = typeName;
        IsPartial = isPartial;
        IsSealed = isSealed;
        EnclosingTypes = enclosingTypes;
        NonPartialEnclosingTypeNames = nonPartialEnclosingTypeNames;
    }

    /// <summary>The endpoint's namespace, or empty for the global namespace.</summary>
    public string Namespace { get; }

    /// <summary>The endpoint's own type name, without namespace or enclosing types.</summary>
    public string TypeName { get; }

    /// <summary>Whether the endpoint class is declared <c>partial</c>.</summary>
    public bool IsPartial { get; }

    /// <summary>
    ///     Whether the endpoint class is declared <c>sealed</c>, which decides whether generated
    ///     members carry <c>sealed override</c> or plain <c>override</c>.
    /// </summary>
    public bool IsSealed { get; }

    /// <summary>
    ///     The enclosing types, outermost first, that generated code must reopen around the
    ///     endpoint. Empty for a top-level endpoint.
    /// </summary>
    public EquatableArray<EnclosingTypeDeclaration> EnclosingTypes { get; }

    /// <summary>
    ///     The enclosing type names that are not declared <c>partial</c>, so cannot be reopened.
    ///     SYNE020 reports each of them.
    /// </summary>
    public EquatableArray<string> NonPartialEnclosingTypeNames { get; }
}
