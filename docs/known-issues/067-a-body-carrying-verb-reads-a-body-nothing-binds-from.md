# [Bug]: A body-carrying verb reads a body nothing binds from

**Severity:** Medium
**Area:** Generator
**Discovered on:** `feat/synapse-endpoints`, .NET 10, while adding an `Accepted()` example to `examples/EndpointsApi`
**Status:** ✅ **Resolved** on `feat/synapse-endpoints` — see [Resolution](#resolution).

> **TL;DR.** The generated binder read a JSON body whenever the verb was one that carries a body, even
> when every property bound from the route, so such an endpoint answered `400` unless the caller sent
> `{}`; what decides the read is now the binding rather than the verb.

---

## Describe the bug

`BinderEmitter` decided whether to read a request body with:

```csharp
var isBodyless = boundType.IsBodylessVerb && !hasBodyProperty;
```

Read as "skip the read only when the verb never carries a body **and** nothing resolved to `Body`".
The second clause is the one that matters: if no property binds from the body, there is nothing to
deserialize, whatever the verb. The `&&` meant a `POST` or `PUT` whose every property came off the
route read a body regardless — and `ReadJsonBodyAsync` treats an absent body as a failure, so the
endpoint answered `400` to the request that is natural for it.

```
POST /tasks/{taskId}/archive        →  400  "The request body is required to be JSON, but the
                                            request declared content type ''."
POST /tasks/{taskId}/archive  {}    →  202
```

Two further consequences followed from the same line, because `isBodyless` also gates how the message
is constructed:

- The message was constructed by the deserializer rather than by the binder, so a route-bound property
  could not be `required` — `System.Text.Json` demanded a member the payload never carries. That is
  the limitation [061](061-required-bound-property-does-not-compile.md) lifted for bodyless verbs and
  that remained in force here for no reason anyone chose.
- `SYNE008` demanded a `[JsonSerializable]` registration for a request type that, once the read is
  gone, never reaches the serializer.

The predicate was also written out three times — in `BinderEmitter`, in `ResolveJsonRequestTypeName`
for `SYNE008`, and, in a different and inconsistent form, in the runtime's `Accepts` declaration,
which went by the verb alone.

---

## Steps to reproduce

1. Declare a command whose only property binds from the route, on a body-carrying verb:

   ```csharp
   public sealed record ArchiveTaskCommand : IRequest<TaskArchived>
   {
       public Guid TaskId { get; init; }
   }

   [Post("/tasks/{taskId:guid}/archive")]
   public sealed class ArchiveTaskEndpoint : Endpoint<ArchiveTaskCommand, TaskArchived>;
   ```

2. `curl -X POST /tasks/{id}/archive` → `400`.
3. `curl -X POST /tasks/{id}/archive -H 'Content-Type: application/json' -d '{}'` → `202`.
4. Mark `TaskId` as `required` and repeat step 3 → `400`, because the deserializer now demands a
   `taskId` field the payload does not carry.

---

## Expected behavior

An endpoint that binds nothing from the request body does not read one, does not require one, and does
not declare one — on any verb. A route-bound property on such a message may be `required`, since the
binder constructs the message itself.

## Actual behavior

The body was read, required, and declared, and `required` was unavailable, purely because of the verb.

---

## Code sample

```csharp
// src/Synapse.Endpoints.Generator/Emit/BinderEmitter.cs — before
var isBodyless = boundType.IsBodylessVerb && !hasBodyProperty;

// src/Synapse.Endpoints/RawEndpoint.Generic.cs — before, and inconsistent with the above
if (!HttpMethodHelpers.AllVerbsAreBodyless(configuration.HttpMethods))
{
    handlerBuilder.Accepts<TRequest>("application/json");
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

The verb had already had its say before this point. It is what rule 4 consults when resolving an
unannotated property to the query string or to the body, so by the time the emitter runs, "does
anything bind from the body?" is the complete answer and the verb is a second, redundant — and here
wrong — input. Consulting it again turned a resolved fact back into a guess.

The runtime's `Accepts` declaration could not consult the same fact at all: an endpoint knows its
verb, not what each property bound from, so it declared a request body from the verb alone. That is
correct for the hand-bound tiers, whose author may read anything, and wrong for the tiers whose binder
the generator writes.

### Resolution

The predicate is now one thing — does any property bind from the body — and is applied consistently:

- `BinderEmitter` reads a body only when some property binds from it. `isBodyless` accordingly now
  means "this binder constructs the message itself", which is what the rest of the method branches on:
  primary-constructor construction, and `required` members set in the object initializer.
- `ResolveJsonRequestTypeName` follows the same rule, so `SYNE008` asks for a registration only where
  the type really reaches the serializer.
- `IEndpointBinder<TRequest>` gains `bool ReadsRequestBody`, a default interface member returning
  `true` so hand-written binders are unaffected. Generated binders state the answer explicitly.
- `RawEndpoint` gains a `DeclaresRequestBody` hook, defaulting to today's verb test. `Endpoint<…>`,
  `MappedEndpoint` and `StreamEndpoint` override it to require **both** that the verb carries a body
  and that the binder reads one — narrowing only, so no endpoint that declared nothing before starts
  declaring one now.
- `BoundTypeInfo.IsBodylessVerb` is removed, having become unused.

The visible loosening is that a body sent to an endpoint that binds nothing from one is now ignored
rather than rejected: with no `Accepts` declaration, routing has no content type to match against, so
nothing answers `415` there. That is the honest description of an endpoint that does not read the
body, and it is pinned by a test.

**Verification.** Nine tests in `test/Synapse.Endpoints.Generator.Tests/BinderEmissionEdgeCaseTests.cs`
cover the new shapes — no body read on `POST`/`PUT`/`PATCH` with a route-only or propertyless message,
a `required` route-bound property set in the object initializer, a positional record constructed
through its primary constructor, `ReadsRequestBody` reported both ways, and the regression guard that a
`POST` with an unannotated property still reads a body. Three in
`test/Synapse.Endpoints.Tests/OpenApiMetadataTests.cs` cover the declaration: suppressed for a binder
that reads nothing, still present for a binder that does not override the member, and still present for
the hand-bound tier. Four in `examples/EndpointsApi.Tests/ResponseMapperTests.cs` cover the wire: both
previously-failing shapes now succeed with no body, a stray body is ignored, and the document declares
a `requestBody` only where one is read. `examples/EndpointsApi` carries a `required` route-bound
property on a `POST` as a live instance. All 738 tests pass, and the Native AOT publish stays free of
IL/RDG warnings.
