using Microsoft.CodeAnalysis;

namespace UnambitiousFx.Synapse.Endpoints.Generator.Diagnostics;

/// <summary>
///     Every diagnostic the endpoints generator can report, kept as one central class rather than
///     constructed inline at each report site (contrast <c>Synapse.Generator</c>'s <c>MDGnnn</c>
///     descriptors, each built where it is reported). A single field per ID keeps the message text
///     consistent and makes every diagnostic discoverable in one place; Tasks 17 and 18 append to
///     this same file rather than starting a second one.
/// </summary>
internal static class EndpointDiagnostics
{
    private const string Category = "Synapse.Endpoints";

    /// <summary>SYNE001: a route template parameter has no matching property on the bound message.</summary>
    internal static readonly DiagnosticDescriptor RouteParameterHasNoProperty = new(
        "SYNE001",
        "Route parameter has no matching property",
        "Route parameter '{0}' has no matching bindable property on '{1}'. Add a property named '{0}', or rename the route parameter.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>SYNE005: the bound message is a streaming request but the endpoint is not a <c>StreamEndpoint</c>.</summary>
    internal static readonly DiagnosticDescriptor StreamMessageOnNonStreamEndpoint = new(
        "SYNE005",
        "Streaming message used with a non-streaming endpoint",
        "'{0}' implements IStreamRequest<T> but this endpoint derives from Endpoint<...>, which dispatches a single response. Derive from StreamEndpoint<...> instead, or stop implementing IStreamRequest<T> if streaming is not intended.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>SYNE006: <c>[InGroup&lt;T&gt;]</c> was declared with a <c>T</c> that does not derive from <c>EndpointGroup</c>.</summary>
    internal static readonly DiagnosticDescriptor InvalidGroupType = new(
        "SYNE006",
        "InGroup type does not derive from EndpointGroup",
        "'{0}' is used as the type argument of [InGroup<T>] on '{1}', but it does not derive from EndpointGroup. Declare '{0}' as a subclass of EndpointGroup, or point [InGroup<T>] at a type that already is one.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    ///     SYNE009: a route attribute and a builder verb/route call inside <c>Configure</c> both
    ///     declare a route for the same endpoint.
    /// </summary>
    /// <remarks>
    ///     Detecting this diagnostic is inherently limited to the direct case: an invocation of
    ///     <c>Get</c>/<c>Post</c>/<c>Put</c>/<c>Patch</c>/<c>Delete</c>/<c>Route</c> directly on the
    ///     <c>Configure</c> method's own builder parameter, found by scanning that method's syntax.
    ///     A verb call reached through a helper method or a local captured in a lambda is not
    ///     detected — the analyzer does not follow control flow across method boundaries — so this
    ///     diagnostic can produce a false negative (silently missing a real conflict) but never
    ///     reports one that is not there.
    /// </remarks>
    internal static readonly DiagnosticDescriptor RouteDeclaredTwice = new(
        "SYNE009",
        "Route declared both by attribute and in Configure",
        "'{0}' declares a route through a route attribute and also calls a route/verb method (Get/Post/Put/Patch/Delete/Route) directly on the builder inside Configure. Remove the route attribute, or remove the call in Configure — only one may declare the route.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
        "Detection covers only a call made directly on Configure's own builder parameter. A verb call " +
        "reached through a helper method or a local captured in a lambda is not followed, so this " +
        "diagnostic can miss a real conflict but will never report one that is not there.");

    /// <summary>
    ///     SYNE010: the endpoint class has a shape that <c>MapEndpoint&lt;TEndpoint&gt;()</c> (which
    ///     requires <c>TEndpoint : EndpointBase, new()</c>) cannot be instantiated for — generic,
    ///     nested inside a generic type, or without a public parameterless constructor.
    /// </summary>
    internal static readonly DiagnosticDescriptor InvalidEndpointShape = new(
        "SYNE010",
        "Endpoint has a shape that cannot be mapped",
        "'{0}' {1}, so 'MapEndpoint<TEndpoint>()' (which requires 'TEndpoint : EndpointBase, new()') cannot be instantiated for it. Make the endpoint a top-level, non-generic class with a public parameterless constructor.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
