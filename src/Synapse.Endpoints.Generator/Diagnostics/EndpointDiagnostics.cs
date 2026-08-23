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

    /// <summary>
    ///     SYNE002: two properties resolve to the same route parameter, the same query key, or more
    ///     than one property carries an explicit <c>[FromBody]</c>. Spec section 4, rule 4.
    /// </summary>
    internal static readonly DiagnosticDescriptor PropertiesClaimSameInput = new(
        "SYNE002",
        "Multiple properties bind the same input",
        "Properties {0} on '{1}' all bind from the same {2}. Only one property may claim a given input — rename the conflicting properties, give them distinct [FromRoute]/[FromQuery] names, or keep only one [FromBody].",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    ///     SYNE007: an explicit <c>[FromBody]</c> property on a verb that never carries a request
    ///     body (<c>GET</c>/<c>DELETE</c>/<c>HEAD</c>), so it can never actually bind at runtime even
    ///     though the generated code compiles and attempts to read it.
    /// </summary>
    internal static readonly DiagnosticDescriptor BodyOnlyPropertyOnBodylessVerb = new(
        "SYNE007",
        "Body-bound property on a bodyless verb",
        "Property '{0}' on '{1}' binds from the request body via [FromBody], but '{2}' requests never carry one, so it can never bind. Remove [FromBody], change the verb, or accept that '{0}' will always be missing.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    ///     SYNE011: a route/query/header-bound property has no accessible setter and its containing
    ///     type is not a record, so the generated binder has no way to apply the parsed value — not
    ///     a direct assignment (no setter) and not a <c>with</c> expression (not a record). Task 15
    ///     already omits such a property rather than emit code that would not compile; this is what
    ///     makes that omission visible. Deliberately scoped to route/query/header — a
    ///     <c>[FromBody]</c>-sourced property is populated by JSON-deserializing the whole message in
    ///     one shot (see <c>BinderEmitter</c>), so neither a setter nor this diagnostic applies to it.
    /// </summary>
    internal static readonly DiagnosticDescriptor UnassignableBoundProperty = new(
        "SYNE011",
        "Bound property cannot be assigned",
        "Property '{0}' on '{1}' binds from the route, query string, or a header, but has no accessible setter and '{1}' is not a record, so its value cannot be applied after binding. Add a setter, or make '{1}' a record so the value can be applied through an init-only 'with' expression.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    ///     SYNE012: a non-string, non-enum route/query/header-bound property's type has no public
    ///     static two-argument <c>TryParse(string, out T)</c>, so the generated binder has no way to
    ///     turn the raw string value into the property's type. Reported at the exact site
    ///     (<c>EndpointsGenerator.ResolveBindableProperty</c>) that already silently omits such a
    ///     property, using the identical condition, so the diagnostic can never disagree with what
    ///     the emitter actually does. Deliberately scoped to route/query/header for the same reason as
    ///     <see cref="UnassignableBoundProperty" /> — a <c>[FromBody]</c>-sourced property is parsed
    ///     by the JSON deserializer, not by a generated <c>TryParse</c> call.
    /// </summary>
    internal static readonly DiagnosticDescriptor UnparsableBoundPropertyType = new(
        "SYNE012",
        "Bound property type cannot be parsed from a string",
        "Property '{0}' on '{1}' has type '{2}', which is not string, not an enum, and has no public static TryParse(string, out {2}) method, so its bound value cannot be parsed. Add a TryParse(string, out {2}) method to '{2}', or change the type of '{0}'.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    ///     SYNE013: a message type is bound by two or more endpoints whose resolved bindings differ
    ///     (different routes and/or verbs produced different <c>BindablePropertyModel</c> sets).
    ///     <c>EndpointRegistry.RegisterBinder&lt;TRequest&gt;</c> is keyed by the message type, so only
    ///     one binder is ever emitted for it — built from whichever endpoint sorts first ordinally by
    ///     fully-qualified name — and every other endpoint sharing the type silently binds using that
    ///     resolution instead of its own. Warning, not Error: this is defined, existing behaviour that
    ///     a consumer may have intended (see <c>EndpointTarget.BoundProperties</c>).
    /// </summary>
    internal static readonly DiagnosticDescriptor ConflictingBindingShapes = new(
        "SYNE013",
        "Message type bound by endpoints with conflicting binding shapes",
        "'{0}' is bound by multiple endpoints with conflicting binding shapes: {1}. Only one endpoint's binding resolution is used for the single registered binder; the others will bind incorrectly at runtime. Give each endpoint its own message type, or ignore this warning if the shared binder's resolution is intentional.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
