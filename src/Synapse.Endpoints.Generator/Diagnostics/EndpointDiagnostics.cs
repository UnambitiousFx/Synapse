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

    /// <summary>
    ///     SYNE003: a <c>POST</c> or <c>PUT</c> endpoint that returns a value (<c>Endpoint&lt;TRequest,TResponse&gt;</c>)
    ///     neither overrides <c>OnSuccess</c> nor calls a declarative success method
    ///     (<c>Ok</c>/<c>Created</c>/<c>Accepted</c>/<c>NoContent</c>/<c>StatusCode</c>) in <c>Configure</c>,
    ///     so the response always falls through to the default <c>200 OK</c> — plausible for <c>PUT</c>,
    ///     almost certainly not what was intended for a <c>POST</c> that created something.
    /// </summary>
    /// <remarks>
    ///     Detection is limited to the direct case, matching SYNE009 (Task 16): a declarative call is
    ///     recognized only when made directly on <c>Configure</c>'s own builder parameter, and only the
    ///     endpoint's own member list is checked for an <c>OnSuccess</c> override — a call reached
    ///     through a helper method, or a base class several levels up that itself supplies the
    ///     override, is not detected. This can produce a false negative (silently missing a real
    ///     explicit mapping) but never reports one that is not there. Info, not Warning: defaulting to
    ///     200 OK is valid, working behavior, not a defect — this is a style nudge, not a warning about
    ///     something wrong.
    /// </remarks>
    internal static readonly DiagnosticDescriptor NoExplicitSuccessMapping = new(
        "SYNE003",
        "POST/PUT endpoint declares no explicit success mapping",
        "'{0}' handles {1} and returns a value, but declares no explicit success mapping — no Ok/Created/Accepted/NoContent/StatusCode call in Configure and no OnSuccess override — so it always responds 200 OK. Call one of the declarative methods on the builder in Configure (Created is typical for POST), or override OnSuccess, to make the response status explicit.",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description:
        "Detection covers only a declarative call made directly on Configure's own builder parameter, " +
        "and an OnSuccess override declared directly on the endpoint's own member list. A call reached " +
        "through a helper method, or an override supplied by an intermediate base class, is not " +
        "detected, so this diagnostic can miss a real explicit mapping but will never report one that " +
        "is not there.");

    /// <summary>
    ///     SYNE004: the endpoint overrides <c>OnSuccess</c> and <c>Configure</c> also calls a
    ///     declarative success method (<c>Ok</c>/<c>Created</c>/<c>Accepted</c>/<c>NoContent</c>/<c>StatusCode</c>).
    ///     <c>EndpointConfiguration.SuccessMapper</c>, set by the declarative call, is checked before
    ///     <c>OnSuccess</c> at dispatch time (see <c>Endpoint&lt;TRequest,TResponse&gt;.CreateDescriptor</c>),
    ///     so the override is silently dead code.
    /// </summary>
    /// <remarks>
    ///     Detection is limited to the direct case, matching SYNE009 (Task 16): the declarative call is
    ///     recognized only when made directly on <c>Configure</c>'s own builder parameter, and the
    ///     <c>OnSuccess</c> override is found only on the endpoint's own member list. A call reached
    ///     through a helper method is not detected, so this diagnostic can miss a real conflict but
    ///     will never report one that is not there.
    /// </remarks>
    internal static readonly DiagnosticDescriptor ConflictingSuccessMapping = new(
        "SYNE004",
        "OnSuccess override conflicts with a declarative success method",
        "'{0}' overrides OnSuccess and also calls a declarative success method (Ok/Created/Accepted/NoContent/StatusCode) on the builder in Configure. The declarative mapping always wins — OnSuccess is never called. Remove the declarative call, or remove the OnSuccess override, so only one declares the success response.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
        "Detection covers only a declarative call made directly on Configure's own builder parameter, " +
        "and an OnSuccess override declared directly on the endpoint's own member list. A call reached " +
        "through a helper method is not detected, so this diagnostic can miss a real conflict but will " +
        "never report one that is not there.");

    /// <summary>
    ///     SYNE008: a type used by an endpoint as its request or response — the exact type as
    ///     declared, not decomposed into a generic collection's element type — is not registered by
    ///     any <c>[JsonSerializable(typeof(...))]</c> attribute on any <c>JsonSerializerContext</c>
    ///     found anywhere in the compilation's reference graph. Without it, the endpoint compiles and
    ///     runs under a JIT-based host, but throws
    ///     <c>NotSupportedException: JsonTypeInfo metadata for type 'X' was not provided by
    ///     TypeInfoResolverChain</c> on first request under Native AOT — a production 500 this
    ///     diagnostic turns into a build-time warning instead.
    /// </summary>
    /// <remarks>
    ///     Reported only when the compilation contains at least one <c>JsonSerializerContext</c>
    ///     anywhere in its reference graph (this assembly's own declarations, or a
    ///     <em>non-framework</em> referenced assembly's — see below) — an app that has not opted
    ///     into source-generated JSON at all is not the target of this advice. The request type is
    ///     checked only when it is actually deserialized from the request body (a non-bodyless verb,
    ///     or an explicit <c>[FromBody]</c> property); query/route/header-only requests never reach
    ///     the JSON deserializer, so requiring their registration would be a false positive. A
    ///     generic collection response/request (for example <c>IReadOnlyList&lt;TaskDto&gt;</c>) is
    ///     checked as that exact closed type, not its element type — that is the type the source
    ///     generator must be told about, and registering only the element type does not, by itself,
    ///     make the collection type serializable. A custom <c>TypeInfoResolverChain</c> entry that is
    ///     not itself a <c>JsonSerializerContext</c> cannot be seen by this check at all — that gap,
    ///     not a defect in the check, is why this is a Warning rather than an Error.
    /// </remarks>
    /// <remarks>
    ///     A type is never reported when it is owned by a framework assembly — this covers
    ///     <c>string</c>, every numeric primitive, <c>bool</c>, <c>char</c>, <c>object</c>,
    ///     <c>Guid</c>, <c>DateTime</c>, <c>DateTimeOffset</c>, <c>TimeSpan</c>, <c>Uri</c>,
    ///     <c>Half</c>, <c>Int128</c>/<c>UInt128</c>, <c>Version</c>, <c>byte[]</c> and any future
    ///     built-in-supported BCL type, since the source-generated resolver supports these
    ///     intrinsically without any <c>[JsonSerializable]</c> entry — see
    ///     <c>EndpointsGenerator.IsFrameworkOwned</c>. This is a structural rule (is the type's own
    ///     assembly a framework assembly?), not a hardcoded list of "known" types, precisely because a
    ///     hardcoded list is a losing game against every future .NET release. A generic collection is
    ///     still reported when framework-owned itself but closed over a non-framework type argument
    ///     (<c>IReadOnlyList&lt;ThingDto&gt;</c> is reported even though <c>IReadOnlyList&lt;T&gt;</c>
    ///     itself lives in a framework assembly) — otherwise every generic collection of a consumer's
    ///     own type would be silently exempt, which would defeat the collection check above entirely.
    ///     This deliberately means SYNE008 does not, and will not, flag <c>ProblemDetails</c> or
    ///     <c>HttpValidationProblemDetails</c> — both framework types that genuinely need registering
    ///     for Native AOT. That is an intentional scope boundary, not an oversight: this diagnostic
    ///     covers the endpoint's own declared request/response type, not every type reachable from
    ///     the response pipeline or the OpenAPI document. The <c>ProblemDetails</c>/
    ///     <c>HttpValidationProblemDetails</c> obligation is documented separately (Task 24).
    /// </remarks>
    /// <remarks>
    ///     A referenced assembly whose name starts with <c>System.</c>/<c>Microsoft.</c> (or is
    ///     <c>netstandard</c>/<c>mscorlib</c>/<c>WindowsBase</c>) never counts toward "does at least
    ///     one context exist" — see <c>EndpointsGenerator.IsFrameworkAssembly</c>. This was added
    ///     after finding, empirically, that <c>Microsoft.AspNetCore.App</c> alone ships eleven of its
    ///     own internal <c>JsonSerializerContext</c>-derived types
    ///     (<c>Microsoft.AspNetCore.Http.ProblemDetailsJsonContext</c> among them). Without this
    ///     filter this diagnostic would be reported on almost every endpoint's response type in
    ///     almost every ASP.NET Core application, whether or not that application had opted into
    ///     source-generated JSON at all — exactly the false-positive failure mode this diagnostic
    ///     exists to avoid. This filter is deliberately *not* also applied to "what does any context
    ///     register" — every <c>[JsonSerializable]</c> registration found anywhere in the graph
    ///     counts as registered regardless of which assembly hosts the context that declares it,
    ///     because a type that genuinely is registered somewhere must never be reported as missing
    ///     (fix round 1, Task 18 review: applying the same filter to both questions created exactly
    ///     that compound false positive).
    /// </remarks>
    internal static readonly DiagnosticDescriptor MissingJsonSerializableRegistration = new(
        "SYNE008",
        "Type used by an endpoint is missing from every JsonSerializerContext",
        "'{0}' is used as a request or response type by an endpoint, but is not registered by [JsonSerializable(typeof({0}))] on any JsonSerializerContext in the compilation. Add it, or Native AOT publishing will throw 'JsonTypeInfo metadata for type '{0}' was not provided by TypeInfoResolverChain' at runtime on first request.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
        "Reported only when at least one JsonSerializerContext exists in a non-framework assembly " +
        "anywhere in the compilation's reference graph (System./Microsoft.-named assemblies never " +
        "open this gate on their own, since the ASP.NET Core shared framework ships several " +
        "JsonSerializerContexts of its own) -- though a registration from such an assembly still " +
        "counts once the gate is open elsewhere. Checks the exact request/response type as declared " +
        "(a generic collection is checked as that closed type, not its element type). A request type " +
        "is checked only when it is actually deserialized from the body. A type owned by a framework " +
        "assembly is never reported -- this covers string, numeric primitives, Guid, DateTimeOffset, " +
        "Half, Version, byte[] and any future BCL type, via a structural rule rather than a " +
        "hardcoded list, though a generic collection closed over a non-framework type argument is " +
        "still reported even when the collection type itself is framework-owned. This deliberately " +
        "excludes ProblemDetails/HttpValidationProblemDetails from this check's scope -- see Task 24 " +
        "for that separate obligation. A custom TypeInfoResolverChain entry that is not a " +
        "JsonSerializerContext is invisible to this check, which is why this is a Warning rather " +
        "than an Error.");
}
