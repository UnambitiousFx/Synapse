# [Bug]: `Invoker` overloads resolve handlers inconsistently (static vs runtime type) — ✅ RESOLVED

**Severity:** Medium
**Status:** ✅ Resolved on `feature/typed-pipeline-behaviors` — see [Resolution](#resolution).
**Area:** Core (`Invoker`)
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10

---

## Describe the bug

`Invoker` resolves handlers differently between its two `InvokeAsync` overloads:

- The **response-returning** overload looks the handler up by `request.GetType()` (runtime type) in
  `options.Value.RequestDispatchers`, and throws a friendly "no handler registered for request type"
  message when missing.
- The **no-response** overload calls `resolver.GetRequiredService<IRequestHandler<TRequest>>()` using
  the **static** generic `TRequest`, with no friendly error.

So the same conceptual operation has two different resolution semantics. A request passed via a base
class or interface static type resolves correctly on the response path but fails on the no-response
path.

---

## Steps to reproduce

1. Hold a request by a base/interface static type:

   ```csharp
   IRequest cmd = new DoThingCommand();
   await invoker.InvokeAsync(cmd, ct); // TRequest inferred as IRequest, not DoThingCommand
   ```

---

## Expected behavior

Both overloads resolve the handler by the request's runtime type and produce the same friendly
"no handler registered" diagnostic when none is found.

---

## Actual behavior

The no-response overload resolves `IRequestHandler<IRequest>` (the static type), which is not
registered, and throws a raw DI `InvalidOperationException` — while the response overload would have
resolved the concrete handler by runtime type.

---

## Root cause

`src/Synapse/Invoker.cs`:

- Response path (`:15-21`): `options.Value.RequestDispatchers[request.GetType()]` with a friendly
  error.
- No-response path (`:30-32`): `resolver.GetRequiredService<IRequestHandler<TRequest>>()` using
  static `TRequest`.

---

## To address

- Make the no-response overload route through the same runtime-type dispatcher lookup as the
  response overload (and produce the same diagnostic).
- Add a test that invokes a request via a base/interface static type through both overloads.

## Resolution

Added a runtime-type dispatcher dictionary for no-response requests, mirroring the existing
response and stream dispatchers (AOT-safe — the delegate closes over the concrete `TRequest` at
registration, no reflection at dispatch).

- `InvokerOptions.VoidRequestDispatchers` (`Dictionary<Type, Delegate>`, delegate shape
  `Func<IRequest, IDependencyResolver, CancellationToken, ValueTask<Result>>`).
- The no-response `Invoker.InvokeAsync<TRequest>` now looks the dispatcher up by
  `request.GetType()` and throws the same friendly *"No handler registered for request type …"*
  diagnostic as the response overload, instead of resolving `IRequestHandler<TRequest>` by the
  static generic type.
- `SynapseConfig` and `DefaultDependencyInjectionBuilder` populate `VoidRequestDispatchers` from
  their no-response `RegisterRequestHandler` / `RegisterRequestHandlerWhen` paths and merge them in
  `AddRegisterGroup`. No generator change was needed: generated RegisterGroups call
  `builder.RegisterRequestHandler<Handler, Request>()`, which now records the dispatcher.

A request held by a base/interface static type (`IRequest cmd = new DoThingCommand();`) now resolves
by its runtime type on both overloads. Covered by new tests in
`test/Synapse.Tests/Senders/InvokerTests.cs`.

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
