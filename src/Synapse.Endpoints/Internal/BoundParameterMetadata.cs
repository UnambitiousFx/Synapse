namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>Where one bound parameter is read from, in OpenAPI's vocabulary.</summary>
/// <remarks>
///     Deliberately not <c>BindingSource</c>: that enum is the generator's model of where a value
///     comes from and includes <c>Form</c> and <c>Body</c>, which are request-body concerns and are
///     never parameters. This enum holds only the locations OpenAPI calls parameters.
/// </remarks>
public enum BoundParameterLocation
{
    /// <summary>A route value. ASP.NET Core also infers these from the route template.</summary>
    Path,

    /// <summary>A query-string key.</summary>
    Query,

    /// <summary>A request header.</summary>
    Header
}

/// <summary>
///     Describes one non-body input an endpoint's binder reads, so a document generator can declare
///     it as an OpenAPI parameter.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately free of any <c>Microsoft.OpenApi</c> type. This assembly carries no reference
///         to <c>Microsoft.AspNetCore.OpenApi</c> — it is not part of the shared framework, and adding
///         it would push the dependency onto every consumer, including those that never generate a
///         document. The translation into an <c>OpenApiParameter</c> therefore happens in
///         <c>UnambitiousFx.Synapse.Endpoints.OpenApi</c>, which does reference it.
///     </para>
///     <para>
///         Read once per endpoint at startup and again during document generation. Never on a request
///         path.
///     </para>
/// </remarks>
public sealed class BoundParameterMetadata
{
    /// <summary>The key the binder reads — the route parameter name, query key or header name.</summary>
    /// <remarks>
    ///     The binder's <c>SourceKey</c> verbatim, not the property name. Renaming it in the document
    ///     would advertise a key the endpoint does not accept, which is the class of bug this type
    ///     exists to fix.
    /// </remarks>
    public required string Name { get; init; }

    /// <summary>Where the value is read from.</summary>
    public required BoundParameterLocation Location { get; init; }

    /// <summary>Whether a request omitting this value is rejected by the binder.</summary>
    public required bool Required { get; init; }

    /// <summary>Whether the parameter repeats, so its schema is an array.</summary>
    public required bool IsArray { get; init; }

    /// <summary>
    ///     The scalar CLR type, or the element type when <see cref="IsArray" /> is
    ///     <see langword="true" />.
    /// </summary>
    /// <remarks>
    ///     Never a collection type. The array-ness is carried by <see cref="IsArray" /> so a consumer
    ///     never has to unwrap <c>List&lt;T&gt;</c> against <c>T[]</c> against
    ///     <c>IReadOnlyList&lt;T&gt;</c> against <c>IEnumerable&lt;T&gt;</c> — four shapes the binder
    ///     already reduced to one.
    /// </remarks>
    public required Type ValueType { get; init; }
}
