# [Bug]: A route declared in `Configure` emits a request-body read, failing every request to the endpoint

**Severity:** High
**Area:** Generator (`src/Synapse.Endpoints.Generator`)
**Discovered on:** `feat/synapse-endpoints`, .NET 9/10, whole-branch review
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** An endpoint with no route attribute gave the generator an empty HTTP method string, which
> matched no bodyless verb, so the emitted binder read a JSON request body for what is in practice a
> `GET`; an empty method is now treated as bodyless, and the new SYNE014 warns whenever a property's
> binding source actually rested on that assumption.

---

## Describe the bug

`docs/docs/endpoints.mdx` documents the "computed route" escape hatch: an endpoint whose route cannot
be a constant expression omits the route attribute entirely and declares the route inside `Configure`
(`builder.Get($"/computed/{suffix}")`). The runtime honours that — `EndpointMetadata.IsRouteDeclaredInConfigure`
is true and `EndpointBuilder.Build()` resolves route and verb from the builder, so by request time
`configuration.HttpMethods` is `["GET"]`.

The generator did not. `EndpointsGenerator` derives "is this verb bodyless?" from the *attribute's*
verb string, which for such an endpoint is `""`. `"" is "GET" or "DELETE" or "HEAD"` is false, so
`BinderEmitter`'s `isBodyless` was false and the emitted binder opened with
`ReadJsonBodyAsync<T>(context)`. Every request to the endpoint therefore failed in the binder, before
the handler ran:

- with no `Content-Type` header: `500`, `InvalidOperationException: Unable to read the request as JSON
  because the request content type '' is not a known JSON content type`;
- with `Content-Length: 0`: `400`, "The request body is required but was empty or null."

The same wrong belief was visible at build time: SYNE008 demanded the *request* type be registered in
a `JsonSerializerContext`, which it only asks for types that actually reach the JSON deserializer.

No generator test covered an endpoint with no route attribute at all, and the example application had
no such endpoint, which is what let the shape ship broken.

---

## Steps to reproduce

1. Declare an endpoint with no route attribute that declares its route in `Configure`, over a message
   with any bindable property.
2. Build — SYNE008 is reported for the *request* type.
3. `GET /computed/...?filter=abc` — `500`.

---

## Expected behavior

The request binds from the query string and the endpoint responds `200`.

---

## Actual behavior

`500 Internal Server Error` with `InvalidOperationException: ... content type '' is not a known JSON
content type` (or `400` "request body is required" when the client sends `Content-Length: 0`).

---

## Code sample

```csharp
public sealed record ComputedQuery : IRequest<int>
{
    public string? Filter { get; init; }
}

public sealed class ComputedEndpoint : Endpoint<ComputedQuery, int>
{
    public override void Configure(IEndpointBuilder<int> builder)
    {
        builder.Get($"/computed/{Environment.GetEnvironmentVariable("SUFFIX") ?? "default"}");
    }
}
```

Generated before the fix:

```csharp
// SynapseEndpointBinders.g.cs — a body read on what is a GET at runtime.
var body = await BindingHelpers.ReadJsonBodyAsync<global::TestNs.ComputedQuery>(context);
```

---

## Library version

`feat/synapse-endpoints` (pre-release; `Synapse.Endpoints` / `Synapse.Endpoints.Generator` not yet
published)

## .NET version

.NET 9.0, .NET 10.0 (generator project targets `netstandard2.0`)

## Operating system

macOS (Darwin), reproducible on any platform

---

## Additional context

### Root cause

Three sites computed `httpMethod is "GET" or "DELETE" or "HEAD"` from the attribute's verb string, a
value that is legitimately empty for the documented escape hatch. The two halves of the library
disagreed about the same endpoint: the generator resolved binding sources as though the verb carried a
body, and the runtime resolved a `GET`.

### Resolution

Centralised the decision in `EndpointsGenerator.IsBodylessVerb(string)`, which returns true for an
empty method as well as for the bodyless verb set, and `IsDeclaredBodylessVerb(string)` for the
diagnostics (SYNE007) that name the verb in their message and must not fire off an assumption. An
unannotated property on such an endpoint therefore resolves to `Query`, no body read is emitted, and
SYNE008 no longer asks for the request type.

Because the assumption is wrong for a computed `POST`, added **SYNE014** (Warning): reported when a
route declared in `Configure` left at least one property's binding source resting on the assumed verb
— that is, resolved by the verb-dependent convention rather than an explicit `[From*]` attribute or a
route-parameter name match. An endpoint whose properties are all annotated stays silent.

Settled two adjacent verb questions in the same place: `OPTIONS` and `TRACE` joined the bodyless set
(neither carries a request body, and the docs point at `[HttpEndpoint("OPTIONS", …)]` as the way to
declare them), and the runtime's check became `HttpMethodHelpers.AllVerbsAreBodyless` rather than "any
declared verb is bodyless" — unreachable today, since both paths into `EndpointConfiguration.HttpMethods`
produce exactly one verb, but settled while the question was free.

**Verification.** Reproduced against the real example application before the fix
(`GET /tasks/search?title=findable` → `500`, captured with the full stack trace) and passing after
(`200`, with the bound value asserted). Added `SearchTasksEndpoint` to `examples/EndpointsApi` and
`TaskEndpointsTests.Get_ForRouteDeclaredInConfigure_BindsTheQueryValueAndReturns200`, plus generator
tests for the no-attribute binder shape, the SYNE008 false positive, the widened bodyless verb set
(and the body-carrying verbs that must keep reading one), and SYNE014 firing and staying silent. The
Native AOT smoke test covers the endpoint end to end. `dotnet build Synapse.slnx` zero warnings; all
suites green.
