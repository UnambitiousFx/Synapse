# [Bug]: `Microsoft.AspNetCore.Mvc`'s `[FromHeader]` is ignored, and the property binds from the query string

**Severity:** Medium
**Area:** Generator (`src/Synapse.Endpoints.Generator`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing what the review had left unaddressed
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `ResolveSource` recognised `Microsoft.AspNetCore.Mvc`'s `FromRoute`, `FromQuery` and
> `FromBody`, but for headers only Synapse's own `FromHeaderAttribute`. MVC's `[FromHeader]` therefore
> matched nothing, fell through to the binding convention, and on a bodyless verb bound the property
> from the **query string** under its property name. `[FromHeader(Name = "If-Match")]` emitted
> `TryGetQuery(context, "IfMatch", …)`: the header was never read, and no diagnostic said so.

---

## Describe the bug

Binding rule 2 is "an explicit `[From*]` attribute pins the source". The generator implements it with a
switch on the attribute's fully-qualified name, and three of the four entries are MVC's:

```csharp
case "Microsoft.AspNetCore.Mvc.FromRouteAttribute":  …
case "Microsoft.AspNetCore.Mvc.FromQueryAttribute":  …
case "UnambitiousFx.Synapse.Endpoints.FromHeaderAttribute": …   // only Synapse's
case "Microsoft.AspNetCore.Mvc.FromBodyAttribute":   …
```

An attribute that matches no case is not an error and not a diagnostic — it is simply not an attribute
as far as source resolution is concerned. The property then falls to rule 4 (bodyless verb ⇒ query
string) or rule 5 (⇒ body), keyed by its property name.

The documentation already covers a *different* symptom of the same duplication: importing both
`Microsoft.AspNetCore.Mvc` and `UnambitiousFx.Synapse.Endpoints` makes `[FromHeader]` ambiguous
(`CS0104`), with `using` alias guidance for resolving it. That case is loud and was handled.

The silent case is the likely one. A message type needs `UnambitiousFx.Synapse.Abstractions` for
`IRequest` and `Microsoft.AspNetCore.Mvc` for `[FromQuery]`/`[FromRoute]`; it does **not** need
`UnambitiousFx.Synapse.Endpoints`, because the endpoint class that carries `[Get]` is usually a
different file. This repository's own `examples/EndpointsApi/Features/Tasks/Messages.cs` opens with
exactly those two usings. In such a file `[FromHeader]` resolves to MVC's with no ambiguity at all, so
there is nothing to alert the author: it compiles, it reads correctly, and it binds the wrong thing.

What the caller sees depends on the property: a nullable one is silently always `null`, so a
conditional-request or tenant header quietly never arrives; a non-nullable one is a `400` naming a
query key the client was never supposed to send.

---

## Steps to reproduce

1. In a file importing `Microsoft.AspNetCore.Mvc` but **not** `UnambitiousFx.Synapse.Endpoints`,
   declare a message with `[FromHeader(Name = "If-Match")] public string? IfMatch { get; init; }`.
2. Bind it from an endpoint on a bodyless verb in another file.
3. Read the generated `SynapseEndpointBinders.g.cs`.

---

## Expected behavior

```csharp
if (BindingHelpers.TryGetHeader(context, "If-Match", out var rawIfMatch))
```

---

## Actual behavior

```csharp
if (BindingHelpers.TryGetQuery(context, "IfMatch", out var rawIfMatch))
```

No `SYNE` diagnostic of any severity.

---

## Code sample

```csharp
// Messages.cs — note which namespaces are imported, and which is not
using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Synapse.Abstractions;

public sealed record GetTaskQuery : IRequest<TaskDto>
{
    [FromHeader(Name = "If-Match")] public string? IfMatch { get; init; }   // Mvc's attribute
}

// Endpoints.cs
using UnambitiousFx.Synapse.Endpoints;

[Get("/tasks/{taskId}")]
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>;
```

---

## Library version

`feat/synapse-endpoints`

## .NET version

.NET 10.0

## Operating system

macOS (platform-independent)

---

## Additional context

### Root cause

Synapse declares its own `FromHeaderAttribute` because MVC's carries model-binding metadata this
library does not use, and because headers are never bound by convention, so the attribute is the only
way to reach one. Reasonable. What does not follow is accepting MVC's attribute for the other three
sources and not for this one: the switch reads as a list of "attributes that pin a source", and one
entry silently narrower than its neighbours is a trap rather than a policy.

The failure is silent because "no case matched" and "no attribute present" are the same state in this
code. There is nowhere to report from — by the time rule 4 assigns the query string, the attribute has
been forgotten.

### Resolution

`Microsoft.AspNetCore.Mvc.FromHeaderAttribute` is now accepted as a header source, with its name read
through the existing `ReadAttributeName` (MVC exposes `Name` as a settable property, so it arrives as a
named argument, unlike Synapse's positional constructor parameter). Both attributes bind a header; the
`CS0104` ambiguity when both namespaces are imported is unchanged and its `using`-alias guidance still
applies, but resolving it either way now produces the same binding.

Considered and rejected: reporting a diagnostic for MVC's attribute instead of honouring it. It would
turn a silent bug into a loud one without making the obvious code work, and the generator already
treats MVC's `From*` family as first-class for every other source.

**Verification.** `BinderConstructionShapeTests.Generate_ForTheMvcFromHeaderAttribute_ReadsTheHeaderAndNotTheQueryString`
uses the two-namespace layout above — the message in a file that imports MVC and not
`UnambitiousFx.Synapse.Endpoints` — and asserts the emitted binder calls `TryGetHeader(context,
"If-Match", …)` and does **not** call `TryGetQuery(context, "IfMatch", …)`. Both assertions fail
against the previous behaviour.
