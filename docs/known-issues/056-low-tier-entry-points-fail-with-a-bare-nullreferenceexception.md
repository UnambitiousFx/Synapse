# [Bug]: The low tier's entry points fail with a bare `NullReferenceException`

**Severity:** Low
**Area:** AspNetCore mapping (`src/Synapse.Endpoints`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** Two new public entry points had no guard. A `HandleAsync` returning `null` was dereferenced
> straight away, and an endpoint whose `HandleAsync`/`BindAsync` was called before it was mapped read a
> null `_configuration`/`_binder` through `!`. Both surfaced as a `NullReferenceException` naming neither
> the endpoint nor the cause — a `500` for the first, and for the second the least helpful possible
> answer to the natural "let me unit-test my endpoint" gesture.

---

## Describe the bug

The low tier made two things public that were internal before, and both can now be reached in a state
the library never had to consider.

**A null result.** `RawEndpoint.HandleAsync` returns `ValueTask<IResult>`, and the sealed descriptor
executes what it returns:

```csharp
var result = await HandleAsync(context, context.RequestAborted);
await result.ExecuteAsync(context);
```

Returning `null` is now a mistake user code can make — a branch that forgets to return, a helper that
returns `null` on a path the author thought unreachable. The dereference produced
`NullReferenceException` out of the request delegate: a `500` whose stack trace points into library
internals and whose message names neither the endpoint nor what to do about it.

**An unmapped endpoint.** Request-time state is created at startup: `CreatePlan` resolves the
configuration and fetches the binder, and it runs when the endpoint is mapped. `HandleAsync` and
`BindAsync` are public, so the obvious thing to try —

```csharp
var endpoint = new MyEndpoint();
var result = await endpoint.HandleAsync(context, CancellationToken.None);
```

— reads `_configuration!` or `_binder!` before either exists. Again `NullReferenceException`, when the
answer the caller needs is "this endpoint has not been mapped, and its state does not exist until it
is".

Neither is a defect in the request path: mapped endpoints serving real requests were never affected.
Both are about what happens the first time somebody holds the new API the wrong way round, which for a
tier whose entire purpose is to be written by hand is worth more than a null dereference.

---

## Steps to reproduce

1. Write a `RawEndpoint` whose `HandleAsync` returns `ValueTask.FromResult<IResult>(null!)`, map it, and
   send it a request.
2. Or construct any mapped-endpoint type directly and call `HandleAsync` on it without mapping it.

---

## Expected behavior

An `InvalidOperationException` naming the endpoint type and what to do — return a result, or map the
endpoint.

---

## Actual behavior

```
System.NullReferenceException: Object reference not set to an instance of an object.
```

---

## Code sample

```csharp
// 1 — a null result
public override ValueTask<IResult> HandleAsync(HttpContext context, CancellationToken ct)
{
    if (!TryDecide(context, out var result)) { return default; }   // default(ValueTask<IResult>) -> null
    return ValueTask.FromResult(result);
}

// 2 — an unmapped endpoint
await new GetTaskEndpoint().HandleAsync(new DefaultHttpContext(), CancellationToken.None);
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

Both are consequences of the two-level refactor rather than pre-existing holes. Before it, the
bind-dispatch-execute body was internal to each `CreateDescriptor`: there was no user-supplied
`IResult` to be null, and the binder was a local in the same method that created it, so it could not be
read early. Making the tier public turned two internal invariants into things a caller can break, and
`!` states an invariant without enforcing it.

### Resolution

`EndpointBase` gained a `private protected Mapped<TState>(TState?)` helper that replaces every `!` on
startup-created state with a failure that explains itself, naming the endpoint type and pointing at
`MapEndpoint<TEndpoint>()`. Applied at all six sites: `RawEndpoint<TRequest, TResponse>`,
`RawEndpoint<TRequest>`, `Endpoint<TRequest, TResponse>`, `Endpoint<TRequest>`, `MappedEndpoint<…>` and
`StreamEndpoint<…>`.

`RawEndpoint.CreateDescriptor` — the one place in the library where a result is executed — rejects a
null result with an `InvalidOperationException` naming the endpoint and the three ways to return
something instead (`TypedResults.Ok`, `TypedResults.NoContent`, `Results.Empty`).

One null check per request against a field the very next line dereferences anyway; nothing measurable,
and the benchmark's allocation figures did not move.

**Verification.** `RawEndpointTests.Invoke_WhenTheHandlerReturnsNull_ThrowsNamingTheEndpointAndTheRemedy`
and `RawEndpointTests.HandleAsync_OnAnUnmappedEndpoint_ExplainsThatItWasNeverMapped` assert the
exception type and that the message names the endpoint — the part that makes it useful.
