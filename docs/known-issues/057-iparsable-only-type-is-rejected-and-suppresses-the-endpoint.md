# [Bug]: A type implementing only `IParsable<T>` is rejected, and the rejection deletes the endpoint

**Severity:** High
**Area:** Generator (`src/Synapse.Endpoints.Generator`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** The bindability gate demanded `TryParse(string, out T)`, but the emitter had *already* been
> taught to emit the three-argument `TryParse(string, IFormatProvider, out T)` when the type offers it.
> A strongly-typed id written the canonical way — `readonly record struct TaskId : IParsable<TaskId>`,
> which mandates only the three-argument overload — was therefore rejected by SYNE012, which cascaded
> into SYNE001, and the blocking error made the generator emit **no source at all**: the endpoint was
> never registered, never mapped, never reachable. The gate now accepts either overload, matching what
> the emitter emits and what ASP.NET Core's own binder accepts.

---

## Describe the bug

`IParsable<T>` is how a parseable type is declared in modern .NET, and it requires exactly one
`TryParse`:

```csharp
static bool TryParse(string? s, IFormatProvider? provider, out TaskId result);
```

The generator decided whether a property's type was bindable with `HasTwoArgumentTryParse`, which looks
for `TryParse(string, out T)` and nothing else. The emitter, since the invariant-culture fix
([050](050-generated-binders-parse-with-the-current-culture.md)), prefers the three-argument overload
wherever the type has it and carries that decision on
`BindablePropertyModel.ParsesWithFormatProvider`. So the gate and the emitter disagreed about the same
type: the emitter could bind it, and the gate never let it through.

The consequence is out of all proportion to the cause:

1. **SYNE012** (error) reports the property as unparsable and omits it.
2. The route parameter that property was going to satisfy now matches nothing, so **SYNE001** (error)
   fires.
3. SYNE001 is a blocking error, which nulls the `EndpointTarget`, so the generator emits nothing for
   the endpoint — no binder, no metadata registration, no group entry.

The build fails, so nothing ships silently. But it fails with two errors that both describe symptoms
rather than the cause, for a message shape that ASP.NET Core binds natively and that this emitter is
perfectly capable of binding. `ParsesWithFormatProvider`'s own documentation says types offering *only*
the two-argument overload "still get that one", which reads as though the three-argument-only case was
meant to work.

---

## Steps to reproduce

1. Declare a strongly-typed id implementing `IParsable<T>` and nothing else.
2. Use it as a route-bound property on a message.
3. Build.

---

## Expected behavior

The property binds through
`TaskId.TryParse(rawTaskId, CultureInfo.InvariantCulture, out valueTaskId)`, and the endpoint is
generated as usual.

---

## Actual behavior

```
error SYNE012: Property 'TaskId' on 'TestNs.GetTask' has type 'TestNs.TaskId', which is not string,
  not an enum, and has no public static TryParse(string, out TestNs.TaskId) method …
error SYNE001: Route parameter 'taskId' has no matching bindable property on 'TestNs.GetTask' …
```

and `SynapseEndpointBinders.g.cs` is not emitted at all.

---

## Code sample

```csharp
public readonly record struct TaskId(Guid Value) : IParsable<TaskId>
{
    public static TaskId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TaskId result)
    {
        if (Guid.TryParse(s, out var value)) { result = new TaskId(value); return true; }
        result = default;
        return false;
    }
}

public sealed record GetTask(TaskId TaskId) : IRequest;

[Get("/tasks/{taskId}")]
public sealed class GetTaskEndpoint : Endpoint<GetTask>;   // emits nothing before the fix
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

SYNE012 was deliberately written as a check on "the exact condition that already decides the omission,
rather than a separately-maintained list of known-good types, so the diagnostic can never disagree with
what the emitter actually does" — the comment above it says so. That reasoning was sound and the
implementation stopped matching it: the invariant-culture change gave the emitter a second parse path
and did not widen the gate that guards it. One predicate grew, its guard did not, and the guard is the
one that decides whether the endpoint exists.

The cascade is what raises the severity. An omitted property is meant to be a survivable, well-reported
degradation (SYNE011 and SYNE012 both say "omit rather than emit code that would not compile"). Here
the omission removed a route parameter's only match, and SYNE001's blocking behaviour turned a
property-level complaint into a deleted endpoint.

### Resolution

The gate accepts either overload:

```csharp
if (!isString && !isEnum &&
    !HasTwoArgumentTryParse(underlying) &&
    !HasFormatProviderTryParse(underlying))
```

`HasFormatProviderTryParse` already existed — it is what the emitter consults — so the fix is to ask it
here too. SYNE012's message was updated to name both overloads and to say that implementing
`IParsable<T>` supplies the second, since the old message told an author to add a `TryParse` they had
already written. The documentation's diagnostics table matches.

**Verification.** `BinderConstructionShapeTests.Generate_ForATypeImplementingOnlyIParsable_BindsItThroughTheInvariantCultureOverload`
asserts the emitted invariant-culture call and that the generated code compiles;
`Generate_ForATypeImplementingOnlyIParsable_ReportsNothing` asserts neither SYNE012 nor SYNE001 fires,
which is the cascade this bug was really about. Both fail against the previous behaviour — the first
because no binder file is emitted to assert on.
