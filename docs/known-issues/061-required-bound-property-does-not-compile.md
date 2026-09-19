# [Bug]: A `required` bound property emits code that does not compile

**Severity:** Medium
**Area:** Generator (`src/Synapse.Endpoints.Generator`)
**Discovered on:** `feat/synapse-endpoints`, .NET 10, reviewing the low-level endpoint tier
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** The binder constructed the message and applied each bound value afterwards, by assignment
> or a `with` expression. Neither satisfies a `required` member, so a message with a `required`
> route/query/header-bound property produced **CS9035** in generated code with no diagnostic to explain
> it. Required properties are now set in the object initializer of the `new` expression — the one call
> site the language accepts — which also retires the documented limitation that `required` "does not
> work here".


> **Later change.** This report's fix covered the case where the binder constructs the message, which
> at the time meant a bodyless verb. [067](067-a-body-carrying-verb-reads-a-body-nothing-binds-from.md)
> widened that case: a body-carrying verb whose every property binds from the route, query or a header
> now constructs the message too, so `required` works there as well. A property beside a body-bound one
> still cannot be `required` — the deserializer constructs that message and demands the member.

---

## Describe the bug

C# enforces `required` at the creation site. `new T()` and `new T(args)` must set every required member
in an object initializer; a later assignment, and a `with` expression on a record, do not count.

The binder's construction step did exactly what does not count:

```csharp
var message = new global::TestNs.GetThing(valueId);
message = message with { Tenant = valueTenant };     // CS9035 on the line above
```

so any `required` bound property broke the build, pointing into `SynapseEndpointBinders.g.cs`. Nothing
in the generator inspected `IsRequired`, so unlike SYNE011 and SYNE012 — which omit a property rather
than emit code that will not compile, and report why — there was no diagnostic and no degradation, just
a compiler error in a file the author did not write.

The parameterless-constructor form was a known limitation, documented in `endpoints.mdx` under
"`required` does not work here" and in the example app's own message types. The positional-record form
was **not** known, and the documentation actively pointed at positional records as the way to
"sidestep the `required` limitation entirely … there is no post-construction assignment step for
`required` to conflict with". That is true only when every bound property is a constructor parameter.
Add one extra `required` member and the assignment step is back.

---

## Steps to reproduce

1. Declare `public sealed record GetThing(int Id) : IRequest { public required string Tenant { get; init; } }`
2. Bind it from `[Get("/things/{id}")]`.
3. Build.

---

## Expected behavior

```csharp
var message = new global::TestNs.GetThing(valueId) { Tenant = valueTenant };
```

---

## Actual behavior

```
error CS9035: Required member 'GetThing.Tenant' must be set in the object initializer or attribute constructor
```

---

## Code sample

```csharp
// Positional record with an extra required member — was CS9035, undocumented:
public sealed record GetThing(int Id) : IRequest
{
    public required string Tenant { get; init; }
}

// Parameterless constructor — was CS9035, documented as unsupported:
public sealed record GetTaskQuery : IRequest<TaskDto>
{
    public required Guid TaskId { get; init; }
}
```

Both now emit an object initializer, `new GetThing(valueId) { Tenant = valueTenant }` and
`new GetTaskQuery() { TaskId = valueTaskId }`.

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

The emitter had two ways to apply a value — direct assignment and `with` — chosen by
`ResolveAssignmentStrategy` on whether the property has an accessible setter or lives on a record. Both
run after construction, because for every shape the emitter previously handled, after construction is
soon enough. `required` is the one modifier that makes *when* a value is applied part of whether the
code compiles, and it had no representation in the model at all.

### Resolution

`BindablePropertyModel.IsRequired` is carried through, and `EmitBinder` collects the required properties
that construction has to cover — those the constructor does not already consume — into an object
initializer appended to the `new` expression, on both construction paths:

```csharp
var message = new T(args) { Req = valueReq };
var message = new T() { Req = valueReq };
```

Those properties are excluded from the post-construction assignment loop, and from the presence-flag
machinery: an initializer assignment is unconditional, so a flag recording whether the value was present
would never be read, and an unread local is a warning in generated code.

Only the bodyless construction path needs this. A body-bound message is constructed by the JSON
deserializer, which satisfies its own required members; the binder then applies route and query values
to the already-constructed instance, and assigning a required member after construction is legal.

One case remains a compile error by necessity: a `required` property that is not bound at all, because
it is `[NotBound]` or because its type is unparsable and SYNE012 omitted it. Construction cannot set
what it has no value for. The accompanying SYNE011/SYNE012 names the property, so the author is not left
with CS9035 alone, and `endpoints.mdx` now states this explicitly rather than claiming `required` does
not work.

**Verification.** `BinderConstructionShapeTests.Generate_ForARequiredPropertyNoConstructorParameterCovers_SetsItInTheObjectInitializer`
asserts the initializer is emitted, that the property is *not* also assigned afterwards, and that the
code compiles; `Generate_ForARequiredPropertyOnAParameterlessConstructor_SetsItInTheObjectInitializer`
covers the previously-documented-as-impossible form. Beyond the harness, `examples/EndpointsApi` now
declares `required` on the route-bound ids of `GetTaskQuery` and `DeleteTaskCommand` — so the shape is
exercised by the example app's 20 integration tests and by the Native AOT smoke test, not only by
generator assertions.
