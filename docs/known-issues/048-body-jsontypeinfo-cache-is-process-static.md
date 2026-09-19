# [Bug]: The request-body `JsonTypeInfo` cache is process-static, so two applications share one's JSON options

**Severity:** Medium
**Area:** AspNetCore mapping (`src/Synapse.Endpoints`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, whole-branch review
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `ReadJsonBodyAsync` held its `JsonTypeInfo<T>` in a `static class BodyTypeInfo<T>` holder
> — one entry per closed `T` per *process* — so two hosts in one process with different
> `ConfigureHttpJsonOptions` silently shared whichever resolved first; the cache is now keyed on the
> request's `JsonSerializerOptions` instance.

---

## Describe the bug

`BindingHelpers.ReadJsonBodyAsync<T>` resolved its `JsonTypeInfo<T>` through
`BodyTypeInfo<T>.Cache`, a `static readonly JsonTypeInfoCache<T>` on a private generic holder. A
static field on a generic type is per closed type argument *per process*, not per application.
`JsonTypeInfoCache.Get` resolves once from the first `HttpContext` it sees and never re-checks.

`ReadFromJsonAsync(request, jsonTypeInfo, ct)` deserializes using the *type info's own* options, so
the second application's configuration was not merely late — it was ignored outright for that type,
for the lifetime of the process. Two `WebApplicationFactory` instances in one test run, or any host
that composes more than one application, hit this.

`StreamEndpoint._itemJson` is an instance field on the endpoint, whose lifetime is the application's,
so the streaming side was already correct; only the body side was global.

---

## Steps to reproduce

1. In one process, build two applications whose `ConfigureHttpJsonOptions` differ (different naming
   policy, converters, or resolver chain).
2. POST the same body type to each.
3. The second application deserializes with the first's options.

---

## Expected behavior

Each application deserializes request bodies with its own configured JSON options.

---

## Actual behavior

The first application to bind a given `T` fixes the options used for `T` for the whole process.

---

## Code sample

```csharp
// Before: one cache per closed T per process.
private static class BodyTypeInfo<T>
{
    internal static readonly JsonTypeInfoCache<T> Cache = new();
}
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

Generated binders reach the body reader through a static call, and `IEndpointBinder` instances live in
the process-wide `EndpointRegistry`, so there is no object with the application's lifetime in reach —
which is what made a process-static holder look like the only option.

### Resolution

Added `Internal.HttpJsonTypeInfo.Resolve<T>(HttpContext)`, which resolves the request's
`JsonSerializerOptions` and looks the type info up in a
`ConditionalWeakTable<JsonSerializerOptions, JsonTypeInfo<T>>` per closed `T`. The options instance is
the key, so entries are per application; a weak table so an application's options and the type infos
resolved from them become collectable together, and because its dependent handles tolerate the value
referencing the key (a `JsonTypeInfo` holds its options), which a strong-valued map would not.
`JsonTypeInfoCache<T>` remains for `StreamEndpoint`'s per-application field and now delegates its
resolution to the same helper, with both types documenting which to reach for.

The cache layer was kept rather than replaced by a bare per-request `options.GetTypeInfo` call because
the measurement said so. The endpoint-dispatch benchmark had no body-reading arm at all — its `GET`
pair never calls `ReadJsonBodyAsync` — so arms for a hand-written `MapPost` lambda and the equivalent
Synapse endpoint were added first. Normalised against the hand-written baseline in the same run:
process-static 0.98 / 0.97, options-keyed 1.04 / 0.96, per-request-uncached 1.05 / 1.12 / 1.06.
Resolving per request costs roughly 0.4–0.5 us (6–10% of that arm), spent in the DI and options
lookups rather than in `GetTypeInfo`; the options-keyed cache measures the same as the static one it
replaces.

**Verification.** Added tests asserting two applications get distinct type infos, each bound to its
own `JsonSerializerOptions` instance, and that the same application resolves the cached instance on a
second call. Benchmarks as above.
