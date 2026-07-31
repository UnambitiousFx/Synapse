# [Bug]: The comma check runs on decoded baggage values

**Severity:** Medium
**Area:** Observability
**Discovered on:** `main`, .NET 10, code review of the v2 trace-context rework
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** `BaggageLimits.IsValidValue` refused commas, but values are percent-encoded on the wire — so a
> spec-conformant peer sending `tenant.name=Acme%2C%20Inc` had the entry unescaped, then rejected for containing
> the comma it had correctly escaped, and `SetBaggage("k", "a,b")` was refused for a value `Format` would have made
> safe.

---

## Describe the bug

```csharp
// src/Synapse.Abstractions/BaggageLimits.cs — before
public static bool IsValidValue(string? value)
{
    …
    foreach (var c in value)
    {
        if (c is ',' || char.IsControl(c))
        {
            return false;
        }
    }
    …
}
```

The comma is the baggage *list* delimiter, so it cannot appear in the **encoded** form — but nothing stops it in
the decoded one, because `BaggageCodec.Format` escapes every value with `Uri.EscapeDataString`. The check was
applied to the decoded value in both directions:

- **Outbound.** `context.SetBaggage("company.name", "Acme, Inc")` returned `false`, and `Format` skipped the entry
  even if it had somehow been stored, although `company.name=Acme%2C%20Inc` is exactly what the specification
  prescribes.
- **Inbound.** `BaggageCodec.Parse` unescapes *before* validating, so a conformant peer's `Acme%2C%20Inc` became
  `Acme, Inc`, failed the check, and was silently dropped — counted in `dropped` and logged as "malformed" when it
  was nothing of the kind.

The same reasoning applies to `=`, which is legal inside a value and likewise escaped; only the key/value
separator position matters, and `Parse` splits on the *first* `=`.

---

## Steps to reproduce

1. `context.SetBaggage("company.name", "Acme, Inc")` — observe `false`.
2. Extract a header of `tenant.name=Acme%2C%20Inc` — observe the entry missing and `dropped` incremented.

---

## Expected behavior

A value may contain any character that survives percent-encoding; only control characters are refused. Keys stay
strict, because they travel unescaped.

---

## Actual behavior

Values containing a comma were refused outbound and silently dropped inbound.

---

## Code sample

```csharp
var context = new Context(identity);

// before: false — although the wire form "Acme%2C%20Inc" is perfectly valid
Console.WriteLine(context.SetBaggage("company.name", "Acme, Inc"));

// before: no entries, dropped == 1
var entries = BaggageCodec.Parse("tenant.name=Acme%2C%20Inc", out var dropped);
```

---

## Library version

`main` (pre-release, v2 development)

## .NET version

.NET 10.0

## Operating system

macOS

---

## Additional context

### Root cause

One validation routine used for two different representations. The rule "no commas" belongs to the encoded form;
`IsValidValue` was only ever called with decoded values.

### Resolution

`IsValidValue` now refuses control characters only, and its XML docs say why delimiters are allowed in a value but
not in a key. Nothing else had to change: `Format` already escaped values, and `Parse` already split on the first
`=` and unescaped afterwards, so the round trip was correct as soon as the gate stopped rejecting it.

**Verification.** `test/Synapse.Tests/Contexts/ContextTests.cs` —
`Context_SetBaggage_WhenValueContainsADelimiter_AcceptsIt` covers `Acme, Inc`, `a=b` and `100% coffee`;
the `Context_SetBaggage_WhenValueIsNotSerializable_ReturnsFalse` theory now covers control characters only.
`test/Synapse.Tests/Propagation/W3CContextPropagatorTests.cs` —
`InjectThenExtract_RoundTripsAValueContainingACommaAndSpaces` asserts the emitted header is
`company.name=Acme%2C%20Inc` and that it parses back to one entry, and
`Extract_WithAnEscapedCommaFromAConformantPeer_KeepsTheEntry` covers the inbound half. All fail against the
previous check.
