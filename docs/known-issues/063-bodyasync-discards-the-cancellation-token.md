# [Bug]: `BodyAsync` accepts a cancellation token and discards it

**Severity:** Low
**Area:** AspNetCore mapping (`src/Synapse.Endpoints`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing what the review had left unaddressed
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `context.BodyAsync<T>(cancellationToken)` took a token and threw it away —
> `_ = cancellationToken;` — reading the body under `HttpContext.RequestAborted` regardless. The
> signature, the documentation table and every call site in the repository said otherwise. The token is
> now linked with `RequestAborted`, so both apply.

---

## Describe the bug

The low level's body reader had this shape:

```csharp
public static ValueTask<BindResult<T>> BodyAsync<T>(this HttpContext context,
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(context);
    _ = cancellationToken;                                  // discarded
    return BindingHelpers.ReadJsonBodyAsync<T>(context);     // reads under RequestAborted
}
```

The discard was deliberate and documented — the `<param>` tag read "Unused; the read is bound to
`HttpContext.RequestAborted`" — which makes it a design decision rather than an oversight. It is still
the wrong one. A parameter that is accepted and ignored is indistinguishable, at the call site, from one
that works: a caller who links a timeout into their token gets no timeout, and nothing tells them.

Nothing was broken by it in practice, because the token a handler has to hand *is*
`context.RequestAborted` — `RawEndpoint.HandleAsync` is passed exactly that, so the overwhelmingly
common call passes a token equal to the one being used anyway. That is what kept it invisible, and also
what makes the parameter worth honouring rather than removing: it already reads as though it works.

The documentation compounded it by listing the member as `BodyAsync<T>(ct)` in the helper table with no
hint that `ct` was inert, and the library's own tests passed `TestContext.Current.CancellationToken`
into it — the idiomatic xunit gesture, and one that did nothing here.

---

## Steps to reproduce

1. In a low-level handler, `await context.BodyAsync<T>(new CancellationTokenSource(TimeSpan.FromMilliseconds(1)).Token)`.
2. Send a slow or large body.
3. The read is never cancelled by that token.

---

## Expected behavior

The read observes the caller's token as well as the request's.

---

## Actual behavior

The caller's token is ignored entirely; only `HttpContext.RequestAborted` cancels the read.

---

## Code sample

```csharp
using var cts = new CancellationTokenSource();
await cts.CancelAsync();

var result = await context.BodyAsync<Payload>(cts.Token);
// before: reads the body and returns a normal BindResult
// after:  throws OperationCanceledException, as an already-cancelled token should
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

`BindingHelpers.ReadJsonBodyAsync<T>` took only an `HttpContext`, because the generated binders that
were its only callers had no token to pass — they run inside `IEndpointBinder.BindAsync(HttpContext)`.
When the low tier gained a public `BodyAsync`, the token was added to the *extension's* signature for
symmetry with every other async API and then had nowhere to go, so it was discarded and the discard
documented.

### Resolution

`ReadJsonBodyAsync<T>` gained an optional `CancellationToken`, and the extension passes what it is
given. The read is bound to `RequestAborted` always, and to the caller's token additionally when they
supplied a distinct cancellable one:

```csharp
if (cancellationToken.CanBeCanceled && cancellationToken != context.RequestAborted)
{
    linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, cancellationToken);
    effective = linked.Token;
}
```

The linked source is allocated only in that branch, so the generated binders — which pass no token, and
the handler case where the token *is* `RequestAborted` — allocate nothing, which is what keeps this off
the hot path measured by `EndpointDispatchBenchmark`.

Removing the parameter was the alternative, and would have been smaller. It was rejected because the
read genuinely can be bounded by more than the request's lifetime, and because a member documented as
taking a token that does nothing is the exact shape of the trap being fixed.

One ripple worth recording: making the token part of the public `ReadJsonBodyAsync` signature caused
xunit's `xUnit1051` analyzer to start flagging the seven existing `BindingHelpersTests` call sites that
pass no token. They now pass `TestContext.Current.CancellationToken`, which is what the analyzer asks
for and what the rest of the suite already did.

**Verification.** `HttpContextBindingExtensionsTests.BodyAsync_HonoursTheCallersCancellationToken`
passes an already-cancelled token and asserts an `OperationCanceledException`; it fails against the
previous behaviour, which read the body and returned a successful `BindResult`. The existing
`BodyAsync_DeserializesTheRequestBody` and
`BodyAsync_ForAnUnusableBody_FailsUnderTheBodyKeyRatherThanThrowing` pin that the ordinary paths are
unchanged.
