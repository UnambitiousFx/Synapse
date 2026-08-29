# [Bug]: NotBound property is still populated from the request body

**Severity:** High
**Area:** Generator
**Discovered on:** `feat/synapse-endpoints`, .NET 10, while adding a `[NotBound]` example to `examples/EndpointsApi`
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** `[NotBound]` excludes a property from the generated route/query/header assignments but not
> from JSON deserialization, so on a body-carrying verb a caller could still set it by naming it in the
> payload; a new SYNE015 warning now says so and names the fix.

---

## Describe the bug

`NotBoundAttribute`'s own documentation states its purpose:

> Excludes a property from binding entirely. Use it for values a pipeline behaviour or handler sets,
> **so a caller cannot supply them by guessing the property name.**

`docs/docs/endpoints/high-level/messages.mdx` repeats the promise against a worked `PUT` example, whose
`[NotBound] ModifiedBy` is described as something "a caller cannot supply by guessing the property
name".

The promise does not hold on any verb that carries a body. `[NotBound]` is honoured where the
generator emits assignments — rule 1 of the five binding rules, applied in `ResolveBindableProperty` —
but a body-carrying verb is not bound property-by-property. The emitted binder populates the message in
one shot:

```csharp
var body = await BindingHelpers.ReadJsonBodyAsync<UpdateThingCommand>(context);
```

`System.Text.Json` has never heard of `[NotBound]`, so it sets the property from whatever the payload
contains. The properties the attribute exists to guard are precisely the ones a caller must not
control — an actor, a tenant, a user id, a timestamp — which is what makes this a privilege question
rather than a tidiness one.

The exclusion does hold on a bodyless verb, where nothing deserializes the message.

---

## Steps to reproduce

1. Declare a message with a `[NotBound]` property on a body-carrying verb:

   ```csharp
   public sealed record PatchTaskCommand : IRequest<TaskPatched>
   {
       public Guid TaskId { get; init; }
       public required string Title { get; init; }

       [NotBound]
       public DateTimeOffset StampedAt { get; init; }
   }

   [Patch("/tasks/{taskId:guid}")]
   public sealed class PatchTaskEndpoint : Endpoint<PatchTaskCommand, TaskPatched>;
   ```

2. Have the handler echo `StampedAt` back, and do not have a pipeline behaviour overwrite it.
3. Send a payload naming the property:

   ```
   curl -X PATCH /tasks/{id} -H 'Content-Type: application/json' \
        -d '{"title":"forged","stampedAt":"2000-01-01T00:00:00Z"}'
   ```

4. The response carries `"stampedAt":"2000-01-01T00:00:00+00:00"` — the caller's value, on a property
   documented as unsettable by callers.
5. Repeat with `[JsonIgnore]` added beside `[NotBound]`: the response carries
   `"0001-01-01T00:00:00+00:00"`, the default.

---

## Expected behavior

Either the caller's value never reaches the message, or the author is told that it can. Since the
generator cannot alter how `System.Text.Json` treats a type it does not own, the second is the
achievable one: the shape should not compile silently.

## Actual behavior

Silent. The build was clean and the caller's value reached the handler.

---

## Code sample

```csharp
// src/Synapse.Endpoints.Generator/EndpointsGenerator.cs — ResolveBindableProperty, before
foreach (var attribute in property.GetAttributes())
{
    if (attribute.AttributeClass?.ToDisplayString() ==
        "UnambitiousFx.Synapse.Endpoints.NotBoundAttribute")
    {
        return null;   // excluded from the emitted assignments — and from nothing else
    }
}
```

---

## Library version

`feat/synapse-endpoints`

## .NET version

.NET 10.0

## Operating system

macOS (Darwin 25.6.0, arm64)

---

## Additional context

### Root cause

Two binding mechanisms, one attribute that only reaches the first. Route, query and header values are
applied by code the generator writes, where rule 1 is enforced by returning `null` and emitting
nothing. The body is applied by `System.Text.Json`, which the generator does not write and cannot
annotate — it can only read what the author declared on the type. `[NotBound]` was therefore always
going to be a partial guarantee on a body-carrying verb, and the documentation stated it as an
unqualified one.

The interaction is easy to miss because the usual usage hides it: the value is normally overwritten by
the pipeline behaviour or handler that owns it, so a forged value is discarded on its way through and
nothing observable goes wrong. It goes wrong when the owner is conditional — stamping only when the
property is still at its default is the natural way to write it, and it hands the caller control.

### Resolution

A new **SYNE015** warning reports a `[NotBound]` property on a message the binder deserializes, unless
the property also carries a `[JsonIgnore]` that actually suppresses deserialization.

- Reported only when the binder really does read a body. The condition mirrors `BinderEmitter`'s own:
  the message is deserialized unless the verb is bodyless *and* no property resolved to `Body`. A
  bodyless verb with no explicit `[FromBody]` stays silent, because `[NotBound]` alone is sufficient
  there.
- `[JsonIgnore]` suppresses the warning only with `Condition = Always`, which is the default when the
  attribute is written with no arguments. `Never`, `WhenWritingDefault` and `WhenWritingNull` govern
  serialization only and leave the property writable from the body, so they do not suppress it.
- Warning rather than Error: the code compiles and runs, and an unconditional overwrite already closes
  the hole, so existing builds are not broken by the upgrade.

The attribute's XML documentation and `messages.mdx` are corrected to state the qualification rather
than the unqualified promise, and `examples/EndpointsApi` carries the correct pattern —
`[NotBound]` + `[JsonIgnore]`, with the behaviour's rewrite unconditional — with the reasoning at the
property.

**Verification.** Nine tests in `test/Synapse.Endpoints.Generator.Tests/NotBoundDiagnosticTests.cs`:
SYNE015 fires on `POST`/`PUT`/`PATCH`, stays silent on `GET`/`DELETE`, fires on a bodyless verb that
carries an explicit `[FromBody]`, is suppressed by a bare `[JsonIgnore]`, is suppressed by
`Condition = Always` and by nothing else, and — the regression guard — `[NotBound]` still emits no
binding for the property, with a route parameter matching a `[NotBound]` property still reporting
SYNE001. Reproduced end to end against the running app before and after, as in the steps above. All
150 generator tests pass, `examples/EndpointsApi` builds warning-free with the correct pattern
applied, and its Native AOT publish stays free of IL/RDG warnings.
