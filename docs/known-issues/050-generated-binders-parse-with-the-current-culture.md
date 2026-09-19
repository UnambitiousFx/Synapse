# [Bug]: Generated binders parse route and query values with the current culture

**Severity:** Medium
**Area:** Generator (`src/Synapse.Endpoints.Generator`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, while adding the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `BinderEmitter` emitted the two-argument `T.TryParse(raw, out var value)`, which reads
> `NumberFormatInfo.CurrentInfo` / `DateTimeFormatInfo.CurrentInfo`, so a `decimal`, `double`,
> `DateTime`, `DateOnly` or `TimeOnly` route or query value was interpreted differently depending on
> the server's locale — and differently from every hand-written Minimal API route in the same
> application, because ASP.NET Core's own parameter binding pins the invariant culture. The emitter
> now passes `CultureInfo.InvariantCulture` wherever the type offers the three-argument overload.

---

## Describe the bug

A route or query value is a wire format: `?amount=1.5` means one and a half, on every host, in every
deployment. The generated binder did not treat it that way.

`BinderEmitter.EmitParseCheckOnly` emitted:

```csharp
if (!global::System.Decimal.TryParse(rawAmount, out var valueAmount))
```

That overload parses with `CultureInfo.CurrentCulture`. On a host whose culture uses a comma decimal
separator (`de-DE`, `fr-FR`, most of continental Europe) `"1.5"` does not parse as `1.5`, and
`"1,5"` does. Dates are worse: `DateTime.TryParse` is heavily culture-sensitive, so `"03/04/2026"`
silently means March 4th on an `en-US` host and April 3rd on an `en-GB` one.

Two things made this hard to notice:

1. `int`, `long`, `Guid` and `bool` — the overwhelmingly common route-parameter types — are
   effectively culture-insensitive for the values a URL carries, so the bug is invisible until
   someone binds a decimal or a date.
2. CI, the test suite and most development machines run under an invariant or `en-US` culture, where
   the current-culture overload and the invariant overload agree.

The divergence from the rest of the application is the sharpest symptom. ASP.NET Core's own
`TryParse`-based parameter binding passes `CultureInfo.InvariantCulture`, so in an application mixing
Synapse endpoints with hand-written `MapGet` routes, the identical query string bound to two
different values depending on which kind of route received it.

---

## Steps to reproduce

1. Declare an endpoint whose bound message has a `decimal` query property.
2. Run the host under a culture with a comma decimal separator (`DOTNET_SYSTEM_GLOBALIZATION_*`
   settings aside, `CultureInfo.CurrentCulture = new CultureInfo("de-DE")` at startup is enough).
3. `GET /prices?amount=1.5`.

---

## Expected behavior

`Amount` binds to `1.5m`, matching what a hand-written `MapGet("/prices", (decimal amount) => …)`
route in the same application does.

---

## Actual behavior

Binding fails with a `400` (`"The query value is not a valid System.Decimal."`), because under
`de-DE` the string `"1.5"` is not a valid decimal. Conversely `?amount=1,5` binds to `1.5m` — a value
no invariant-culture client would ever send.

---

## Code sample

```csharp
public sealed record PriceQuery : IRequest<PriceDto>
{
    [FromQuery] public decimal Amount { get; init; }
}

[Get("/prices")]
public sealed class PriceEndpoint : Endpoint<PriceQuery, PriceDto>;

// Emitted before the fix — reads the server's locale:
//     global::System.Decimal.TryParse(rawAmount, out valueAmount)
//
// Emitted after the fix:
//     global::System.Decimal.TryParse(
//         rawAmount, global::System.Globalization.CultureInfo.InvariantCulture, out valueAmount)
```

---

## Library version

`feat/synapse-endpoints`

## .NET version

.NET 10.0

## Operating system

macOS (reproduced by setting `CultureInfo.CurrentCulture`; platform-independent)

---

## Additional context

### Root cause

`BinderEmitter.EmitParseCheckOnly` built the parse call from the property type name and the raw local
alone, with no format provider. SYNE012 — the diagnostic that decides whether a property type is
bindable at all — checks for `TryParse(string, out T)`, the two-argument shape, so the emitter used
the overload the diagnostic had verified. That kept the two consistent, which is why the emitter was
written that way; it just verified the wrong overload.

Simply emitting the three-argument overload unconditionally would not compile for a custom type that
implements only `TryParse(string, out T)`, which is all SYNE012 requires and all the documentation
promises.

### Resolution

`EndpointsGenerator` gained `HasFormatProviderTryParse`, a sibling of the existing
`HasTwoArgumentTryParse`, which looks for a public static `TryParse(string, IFormatProvider, out T)`
— the shape `IParsable<T>` mandates and every framework numeric and date type implements. The result
is carried on `BindablePropertyModel.ParsesWithFormatProvider`, and
`BinderEmitter.TryParseExpression` emits:

- `T.TryParse(raw, CultureInfo.InvariantCulture, out value)` when the type has that overload;
- `T.TryParse(raw, out value)` when it does not, so a custom type meeting only SYNE012's minimum
  keeps working;
- `Enum.TryParse<T>(raw, out value)` for enums, which are not culture-sensitive.

The new low-level helper surface (`context.TryGetQuery<T>`, `BindingValidator.Query<T>`) pins the
invariant culture the same way, through the `IParsable<T>` constraint, so both endpoint levels agree.

One detail worth recording, because it cost a debugging cycle: the first version of
`HasFormatProviderTryParse` compared `parameter.Type.ToDisplayString()` against
`"System.IFormatProvider"` and never matched. The parameter is declared `IFormatProvider?`, so the
display string carries the nullable annotation. It now matches on the type's name and containing
namespace instead.

**Verification.** `BinderEmissionEdgeCaseTests.Generate_ForPropertyTypeWithNoTryParse_OmitsItWithoutBreakingTheOthers`
asserts the emitted invariant-culture call. `HttpContextBindingExtensionsTests.TryGetQuery_Typed_ParsesWithTheInvariantCulture`
sets `CultureInfo.CurrentCulture` to `de-DE` and asserts that `?amount=1.5&when=2026-08-25` still
binds to `1.5m` and `2026-08-25` — a test that fails against the previous behaviour. Enum emission is
pinned separately by `Generate_ForEnumRouteProperty_ParsesThroughEnumTryParse`, which asserts no
format provider is passed.
