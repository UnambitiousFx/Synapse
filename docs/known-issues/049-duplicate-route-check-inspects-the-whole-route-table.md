# [Bug]: The duplicate-route check inspects the whole route table and blames Synapse for other people's routes

**Severity:** Medium
**Area:** AspNetCore mapping (`src/Synapse.Endpoints`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, whole-branch review
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `MapSynapseEndpoints`'s startup duplicate check walked every `RouteEndpoint` with no
> filter and then threw "More than one **Synapse** endpoint claims…", so two hand-written `MapGet`
> calls were blamed on Synapse and a template disambiguated by a matcher policy threw instead of
> working; `EndpointMapper.Map` now attaches an internal marker and the scan honours it.

---

## Describe the bug

`EndpointRouteBuilderExtensions.ThrowOnDuplicateRoutes` enumerated `endpoints.DataSources`, took every
`RouteEndpoint` it found, and keyed each on `"{method} {rawText}"`. Nothing distinguished Synapse's own
endpoints from the rest of the application's route table. Two consequences:

1. Two hand-written `app.MapGet("/health", …)` calls — a duplicate ASP.NET is itself responsible for
   reporting — produced `InvalidOperationException: More than one Synapse endpoint claims the same HTTP
   method and route: GET /health`, naming a library that never mapped either route.
2. More seriously, a route template *legitimately* duplicated and disambiguated at match time by a
   matcher policy — API versioning being the motivating case, which the design points at the `Raw`
   escape hatch to enable — threw at startup instead of working.

---

## Steps to reproduce

1. Map two non-Synapse routes on the same method and template (through a helper, so ASP0022 does not
   reject it at compile time).
2. Call `app.MapSynapseEndpoints(...)`.

---

## Expected behavior

Duplicates among routes Synapse did not map are none of this check's business.

---

## Actual behavior

`InvalidOperationException: More than one Synapse endpoint claims the same HTTP method and route: GET
/health`.

---

## Code sample

```csharp
var app = WebApplication.CreateSlimBuilder().Build();
MapHealth(app);   // app.MapGet("/health", () => "ok");
MapHealth(app);

app.MapSynapseEndpoints(new SynapseEndpointGroup());   // throws, blaming Synapse
```

---

## Library version

`feat/synapse-endpoints` (pre-release; `Synapse.Endpoints` not yet published)

## .NET version

.NET 10.0

## Operating system

macOS (Darwin), reproducible on any platform

---

## Additional context

### Root cause

The check needed a way to recognise its own endpoints and had none, so it used "every route endpoint"
as a proxy for "every Synapse endpoint" — and then wrote a message asserting the proxy was exact.

### Resolution

`EndpointMapper.Map` — the library's single `Map*` call site — now attaches an internal
`SynapseEndpointMarker` to every endpoint it maps, and `ThrowOnDuplicateRoutes` skips any endpoint
without it. The marker is internal on purpose: a public one would invite consumers to attach it and be
counted by a check that cannot reason about their endpoints.

Two Synapse endpoints disambiguated by a matcher policy are still reported. This check cannot see
matcher policies, so it stays conservative about the endpoints it is actually responsible for; that is
a deliberate limitation, not an oversight.

Also documented, where it matters, that reading `dataSource.Endpoints` materialises endpoints early
and re-runs the conventions accumulated so far — harmless for everything this library adds, all
idempotent metadata, but a side-effecting convention added later would run twice.

**Verification.** Added a test proving a non-Synapse duplicate is ignored, and one proving a real
Synapse duplicate still throws alongside non-Synapse routes. The first was proved to discriminate:
neutralising the marker filter turns it red.
