# [Bug]: Native AOT silently skips every authorization behavior for value-type requests

**Severity:** High
**Area:** Authorization
**Discovered on:** `feat/209-authorization-native-aot`, .NET 10, Native AOT publish of a smoke app
**Status:** ✅ **Resolved** on `feat/209-authorization-native-aot` (PR #212) — see [Resolution](#resolution).

> **TL;DR.** Authorization behaviors were registered as open generics, which Native AOT cannot close over a
> value-type request. They were skipped without an error, so the request ran unauthorized. They are now
> registered closed per handler shape.

---

## Describe the bug

`AddSynapseAuthorization` registered its behaviors as open generics (`typeof(ActorAuthorizationBehavior<,>)` and
the like). A native AOT binary has no JIT to close an open generic over a value type that no code path
instantiated, so the container found no behavior for a `readonly record struct` request and resolved an empty
pipeline. The request reached its handler with no actor, role, scope, request-policy or resource check, and
nothing was thrown or logged.

---

## Steps to reproduce

1. Declare `public readonly record struct Count : IQuery<int>;` with a handler.
2. Call `AddSynapseAuthorization()` and publish the app with `PublishAot=true`.
3. Invoke `Count` as a caller that holds no scope.

---

## Expected behavior

`UnauthorizedFailure` (the default `read` scope is missing).

---

## Actual behavior

The handler ran and returned its value. The same code on the JIT runtime returned the failure.

---

## Code sample

```csharp
public readonly record struct Count : IQuery<int>;

var result = await invoker.InvokeAsync(new Count()); // NativeAOT: success, JIT: UnauthorizedFailure
```

---

## Library version

`feat/209-authorization-native-aot`

## .NET version

.NET 10.0

## Operating system

macOS

---

## Additional context

### Root cause

Open-generic service descriptors are closed at runtime with reflection. Under native AOT that only works for
instantiations the compiler already saw, which never includes a value-type request used only through the
container. The failure mode was a missing registration, not an exception, so the check was fail-open.

### Resolution

Core now exposes an `IRequestRegistrationVisitor` that is called with each closed request / response / stream
shape it registers. `AuthorizationRegistrationVisitor` uses it to register the behaviors closed per shape, which
the trimmer and native AOT keep, and to record the request intent (command, query or other) that picks the
default scope without scanning interfaces by reflection. The intent is stored per `SynapseAuthorizationOptions`
instance, so independent containers in one process do not influence each other. An unknown intent fails closed
with an `InvalidOperationException`.

**Verification.** `examples/AuthorizationAot` (struct query without scope, stream, request policy, resource
policy, role, scope, anonymous) published with native AOT and run in CI, all cases returning the expected
failure; `Synapse.Authorization.Tests`, including `Invoke_SameRequestTypeInTwoContainers_KeepsEachContainersIntent`.
