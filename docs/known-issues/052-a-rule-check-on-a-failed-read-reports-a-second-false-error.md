# [Bug]: A rule check on a failed read reports a second, false error

**Severity:** Low
**Area:** AspNetCore mapping (`src/Synapse.Endpoints`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `BindingValidator`'s readers leave their `out` value at `default(T)` when the read fails,
> so a `Check` written straight after an unguarded read tests a value the caller never sent. A request
> to `/reports` with no `page` at all was answered with **both** `"The query value is required."` and
> `"must be at least 1"` — the second manufactured from `default(int)`. The documentation, the
> `<example>` on `BindingValidator` and the shipped example endpoint all demonstrated the unguarded
> form, so it was the pattern users would copy.

---

## Describe the bug

`BindingValidator.Query<T>` and its siblings report their own error and return `false` when a value is
absent or unparsable. They still assign the `out` parameter — necessarily, since C# requires it — and
what they assign is `default(T)`. A rule check placed after the read therefore runs against `0`,
`Guid.Empty` or `default(DateTime)` whenever the read failed, and adds a message about a value that was
never supplied.

The collector's whole purpose is to answer a bad request once, naming every real problem. A cascading
second message works directly against that: the caller is told two things about `page`, one of which
is true and one of which is fiction, and cannot tell which is which.

The readers return `bool` precisely so the check can be guarded, but nothing in the library said so and
every piece of guidance shipped the unguarded form:

- `docs/docs/endpoints.mdx` — twice, in the "middle level" snippet and in the validator section
- the `<example>` block on `BindingValidator` itself
- `examples/EndpointsApi/Features/Ops/RawEndpoints.cs`, the endpoint the docs point at

The tests did not catch it because
`RawEndpointsTests.GetReports_WithTwoBadInputs_Returns400NamingBothOfThem` asserts on the *keys* of the
`errors` dictionary — that both `page` and `tag` are present — and never on the messages under them.
Accumulation across fields was the thing being pinned; accumulation of nonsense within one field was
invisible to it.

---

## Steps to reproduce

1. Run `examples/EndpointsApi`.
2. `GET /reports` (no query string at all), or `GET /reports?page=nope&tag=x`.
3. Read the `errors` object in the response body.

---

## Expected behavior

```json
{ "page": ["The query value is required."], "tag": ["at least one tag is required"] }
```

---

## Actual behavior

```json
{ "page": ["The query value is required.", "must be at least 1"], "tag": ["at least one tag is required"] }
```

And for a value that was sent but could not be parsed:

```json
{ "page": ["The query value is not a valid System.Int32.", "must be at least 1"] }
```

`page=0` — the case the rule actually exists for — was reported correctly both before and after, which
is why the rule looked like it worked.

---

## Code sample

```csharp
// Before — the form the docs, the XML example and the example app all showed:
var validation = context.Validate();
validation.Query<int>("page", out var page);          // fails: reports "required", leaves page = 0
validation.Check(page >= 1, "page", "must be at least 1");   // 0 >= 1 is false -> second, false error

// After — guarded on the read that feeds it:
if (validation.Query<int>("page", out var page))
{
    validation.Check(page >= 1, "page", "must be at least 1");
}
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

Two correct decisions that interact badly. The readers accumulate rather than short-circuit, which is
the entire point of the collector; and they must assign their `out` parameter on every path, which C#
requires. Together they leave a plausible-looking local that no longer stands for anything the caller
sent, and a `Check` on it is indistinguishable — to the reader of the code — from a `Check` on a value
that bound.

The `bool` return value is the intended guard, and was being discarded at every call site the library
itself shipped.

### Resolution

The fix is at the call site, not in the library: `Check` cannot know whether its condition was computed
from a value that bound, and suppressing rule messages for any field that already carries an error
would throw away legitimate multi-message reporting (a field can genuinely be both too short and
malformed).

So the guard is now documented where someone will meet it and demonstrated everywhere the library
shows the pattern:

- `BindingValidator.Check` carries a `<remarks>` block naming the failure and the idiom.
- The `<example>` on `BindingValidator` and both snippets in `docs/docs/endpoints.mdx` guard the check.
- `TagReportEndpoint` in the example app guards it, with a comment saying what the unguarded form did.

The same edit removed a second-order problem in the docs: an unguarded read placed *before* the values
it constrains reads as though ordering mattered, when what matters is the guard.

**Verification.** Reproduced against the running example application before the change and confirmed
gone after it, on all four shapes: no query string, `?page=nope&tag=x`, `?tag=x`, and `?page=0&tag=x`.
The last still reports `must be at least 1` alone, so the guard did not silence the rule it guards.
`RawEndpointsTests.GetReports_WithAMissingPage_ReportsOnlyThatItIsRequired` pins the message list
rather than the key set, which is the assertion whose absence let this through.
