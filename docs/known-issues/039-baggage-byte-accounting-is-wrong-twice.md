# [Bug]: Baggage byte accounting is wrong in both directions

**Severity:** Medium
**Area:** Observability
**Discovered on:** `main`, .NET 10, code review of the v2 trace-context rework
**Status:** ✅ **Resolved** on `main` — see [Resolution](#resolution).

> **TL;DR.** `BaggageCodec.Parse` charged the byte budget for every repetition of a key even though a repetition
> overwrites, so a header repeating one key exhausted the 8192-byte cap and dropped the valid entries after it; and
> `BaggageLimits.MeasureEntry` measured the *decoded* value, so escaped-heavy baggage passed the check and still
> exceeded the limit on the wire.

---

## Describe the bug

Two independent accounting errors, both about the same 8192-byte cap.

**1. Repeated keys were charged twice (or five times).**

```csharp
// src/Synapse/Propagation/BaggageCodec.cs — before
entries[key] = value;      // overwrites
totalBytes += entryBytes;  // but charges as if it were an addition
```

The dictionary write overwrites, the counter accumulates. A header of `k=<2KB>,k=<2KB>,k=<2KB>,k=<2KB>,k=<2KB>`
holds one 2 KB entry, but `totalBytes` reached 10 KB, so the last repetition was dropped as oversized — and any
genuinely new entry after it was dropped too, its budget already spent on values no longer present.

The entry-count check had the same shape of bug: `entries.Count >= MaxEntryCount` was tested before knowing whether
the key already existed, so an overwrite arriving at a full collection was refused although it adds no entry.

**2. The measurement ignored percent-encoding.**

```csharp
// src/Synapse.Abstractions/BaggageLimits.cs — before
return Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(value) + 2;
```

`Format` writes `Uri.EscapeDataString(value)`, where every byte outside the RFC 3986 unreserved set becomes a
three-character escape. Measuring the decoded value therefore understates the wire length by up to 3×: 3000 commas
measured as 3000 bytes and shipped as 9000. The cap exists precisely because intermediaries may truncate baggage
past 8192 bytes, so understating it defeats the check — the entry is accepted, the header goes out oversized, and
what arrives downstream is whatever survived truncation.

---

## Steps to reproduce

1. Extract a header repeating one key several times, followed by another valid entry — observe the tail missing and
   `dropped` non-zero.
2. `context.SetBaggage("k", new string(',', 3000))` — observe it accepted, then observe the emitted header is
   9000+ bytes.

---

## Expected behavior

A repeated key costs the budget once, the last value winning, and never counts against the entry cap. An entry is
measured as it will appear on the wire, so passing the check guarantees a conformant header.

---

## Actual behavior

Valid entries after a repeated key were dropped, and escaped-heavy baggage exceeded the limit on the wire while
passing the check.

---

## Code sample

```csharp
var chunk = new string('x', 2_000);
var header = $"k={chunk},k={chunk},k={chunk},k={chunk},k={chunk},tail=kept";

// before: dropped == 1 and "k" holds the fourth value, not the fifth
var entries = BaggageCodec.Parse(header, out var dropped);

// before: 3002 — the header actually carries 9002 bytes for this entry
Console.WriteLine(BaggageLimits.MeasureEntry("k", new string(',', 3_000)));
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

The counter was maintained as "add the cost of what I just wrote" rather than "the cost of what is now stored", and
the cost function measured the in-memory representation rather than the serialized one it was named after
(*"the serialized size, in bytes, that the entry contributes to the baggage header"*).

### Resolution

`Parse` now computes the replaced entry's cost and projects the total the way `BaggageCollection` already did, so an
overwrite is budget-neutral and does not consume an entry slot:

```csharp
var replacedBytes = entries.TryGetValue(key, out var existing)
    ? BaggageLimits.MeasureEntry(key, existing)
    : 0;

if (replacedBytes == 0 && entries.Count >= BaggageLimits.MaxEntryCount) { dropped++; continue; }

var projectedBytes = totalBytes - replacedBytes + entryBytes;
```

`MeasureEntry` now measures the encoded value through a new `BaggageLimits.MeasureEncodedValue`, which counts
rather than encodes — so it allocates nothing on a path that runs per entry per boundary crossing. The count follows
from the UTF-8 byte total and the number of unreserved characters, each of which stays one byte while every other
byte expands to three.

This tightens the effective limit for values needing escapes, which is the point: the budget now describes the
header that actually goes out. `BaggageCollection` inherits the correction, since it measures through the same
method.

**Verification.** `test/Synapse.Tests/Propagation/BaggageCodecTests.cs` —
`Parse_WithARepeatedKey_ChargesTheByteBudgetOnce`, `Parse_WithARepeatedKeyAtTheEntryCap_AcceptsTheOverwrite`,
`Parse_WithAnEntryPastTheEntryCap_DropsItAndReportsIt`,
`FormatThenParse_WithAValueNeedingEscapes_MeasuresTheWireFormNotTheDecodedOne` (which asserts the measurement
equals what `Format` emits) and the `MeasureEncodedValue_MatchesWhatEscapingProduces` theory, checked against
`Uri.EscapeDataString` across character classes. The repeated-key and wire-form tests fail against the previous
implementation.
