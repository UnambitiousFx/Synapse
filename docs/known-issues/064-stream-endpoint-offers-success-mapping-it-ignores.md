# [Bug]: A streaming endpoint offers success-mapping methods it silently ignores

**Severity:** Low
**Area:** AspNetCore mapping (`src/Synapse.Endpoints`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `StreamEndpoint<TRequest, TItem>.Configure` took the non-generic `IEndpointBuilder`, which
> carries `NoContent()` and `StatusCode(int)`. A streaming endpoint reads only `Route`, `HttpMethods`
> and `ApplyMetadata` off the resolved configuration and never looks at the success mapper, so both
> calls compiled, set fields nobody read, and did nothing. `Configure` now takes a narrower
> `IStreamEndpointBuilder` that does not offer them, so the mistake is a compile error.

---

## Describe the bug

`IEndpointBuilder` — the non-generic one — was shared by exactly two shapes, and only one of them
honoured all of it:

| Shape | Read from its `EndpointConfiguration` |
|---|---|
| `RawEndpoint<TRequest>` (and `Endpoint<TRequest>`) | `Route`, `HttpMethods`, `ApplyMetadata`, `SuccessMapper`, `DeclaredSuccessStatusCode` |
| `StreamEndpoint<TRequest, TItem>` | `Route`, `HttpMethods`, `ApplyMetadata` — nothing else |

The void arity needs `NoContent()`/`StatusCode(int)`: a `DELETE` answering `202` is ordinary. A
streaming endpoint cannot use them at all — it hardcodes its response:

```csharp
handlerBuilder.WithMetadata(new ProducesResponseMetadata(
    StatusCodes.Status200OK,
    typeof(IAsyncEnumerable<TItem>),
    ["application/json", "text/event-stream"]));
```

So a caller got a method that compiled and had no effect whatsoever:

```csharp
public override void Configure(IEndpointBuilder builder)
{
    builder.Summary("Stream every task");   // works
    builder.StatusCode(202);                // compiles, sets fields nobody reads, does nothing
}
```

No compiler error, no analyzer diagnostic, no runtime effect.

---

## Steps to reproduce

1. Derive from `StreamEndpoint<TRequest, TItem>` and override `Configure`.
2. Call `builder.StatusCode(202)` or `builder.NoContent()`.
3. Request the route: it still answers `200` with the streamed body, and `/openapi/v1.json` still
   documents `200`.

---

## Expected behavior

A method that cannot work at this tier is not offered, so calling it does not compile.

---

## Actual behavior

It compiles and is silently ignored.

---

## Code sample

```csharp
// Before — compiles, no effect:
public override void Configure(IEndpointBuilder builder) => builder.NoContent();

// After — CS1061: 'IStreamEndpointBuilder' has no method 'NoContent'
public override void Configure(IStreamEndpointBuilder builder) => builder.NoContent();
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

An interface shared by two tiers whose contracts differ. `IEndpointBuilder` was built for the void
arity and reused for streaming because the two need the same routing and metadata surface; the success
half came along uninspected.

The judgment had already been made elsewhere and simply not applied here. `IRawEndpointBuilder` omits
the same two methods, and says why in its own documentation: they "set a success mapper that a
low-level endpoint never consults — it returns its own result — so offering them would be a trap
rather than a convenience." Substitute "streaming endpoint" and the sentence still holds.

Unlike [054](054-bodyless-success-mapper-declares-a-json-body.md), nothing was ever mis-declared:
behaviour and document both said `200`, so no client was misled. The only casualty was the developer's
expectation, which is why this is filed Low.

Honouring the calls instead was rejected. `NoContent()` contradicts the base class outright — the point
of a stream is a body of many items — and while `StatusCode(202)` could technically be written before
the first item, a streamed body under `202 Accepted` is incoherent, and the status belongs to the
stream writers that set the content type and begin writing. Ignoring these calls is the *correct*
behaviour; offering them was the mistake.

### Resolution

New `IStreamEndpointBuilder`, and `StreamEndpoint.Configure` takes it instead of `IEndpointBuilder`.
It keeps routing (`Route`, `Get`, `Post`), metadata (`Tag`, `Summary`, `Description`, `Name`),
authorization (`RequireAuthorization`, `AllowAnonymous`) and `Raw` — and offers no success mapping at
all, so `NoContent`, `StatusCode`, `Ok`, `Created` and `Accepted` are compile errors rather than
no-ops.

Its implementation, `StreamEndpointBuilder`, is backed by the same `EndpointBuilderCore` as every other
builder, so the "route from the attribute, else from `Configure`, else throw" rule stays in one place.
It resolves to a `RawEndpointPlan` rather than an `EndpointConfiguration<TResponse>`, which removes the
`EndpointBuilder<Unit>` that `StreamEndpoint` previously constructed purely to throw most of it away —
`Unit` is no longer involved in streaming at all.

No existing endpoint broke: nothing in the repository overrode `StreamEndpoint.Configure`, and the
parameter type is the only change to the public shape.

**Verification.** `StreamEndpointTests.StreamEndpointBuilder_OffersNoSuccessMapping` asserts by
reflection that `IStreamEndpointBuilder` declares no `NoContent`, `StatusCode`, `Ok`, `Created` or
`Accepted` — so re-introducing one is a failing test rather than a returning trap — and
`StreamEndpointBuilder_KeepsRoutingAndMetadata` asserts the ten members that did survive, so the
narrowing cannot quietly take something useful with it.
`CreateDescriptor_ForAStreamEndpointDeclaringItsRouteInConfigure_ResolvesIt` covers route resolution
through the new builder. The three byte-exact SSE/JSON-array tests that guard the streaming wire format
passed **unedited** on net8.0, net9.0 and net10.0, which is the gate any change to this class has to
clear.
