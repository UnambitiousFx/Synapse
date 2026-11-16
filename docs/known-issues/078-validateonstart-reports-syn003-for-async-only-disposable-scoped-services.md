# [Bug]: ValidateOnStart reports SYN003 when a scoped service is only IAsyncDisposable

**Severity:** Medium
**Area:** Pipeline / CQRS
**Discovered on:** issue [#223](https://github.com/UnambitiousFx/Synapse/issues/223), .NET 10
**Status:** ✅ **Resolved** on `fix/223-async-disposable-scopes` — see [Resolution](#resolution).

> **TL;DR.** Synapse disposed some internal scopes with a synchronous `Dispose`, which throws when the scope
> created a service that implements only `IAsyncDisposable`. Those scopes now dispose with `DisposeAsync`.

---

## Describe the bug

`ValidateOnStart()` / `ValidateSynapse()` reports `SYN003` for every pipeline whose graph contains a scoped
service that implements `IAsyncDisposable` but not `IDisposable`, and the host fails to start. Requests sent
through `IInvoker` inside an async scope work.

---

## Steps to reproduce

1. Register a scoped service that implements only `IAsyncDisposable` and inject it into a request handler.
2. `services.AddSynapse(cfg => { cfg.AddRegisterGroup(...); cfg.ValidateOnStart(); });`
3. Start the host.

---

## Expected behavior

The validation resolves the pipeline, disposes the scope with `DisposeAsync`, and reports no issue.

---

## Actual behavior

`SynapseValidationException` with one `SYN003 … InvalidOperationException: '<type>' type only implements
IAsyncDisposable. Use DisposeAsync to dispose the container.` per request that depends on the service.

---

## Code sample

```csharp
public sealed class SharedConnection : IAsyncDisposable
{
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

services.AddScoped<SharedConnection>();
services.AddSynapse(cfg => cfg.AddRegisterGroup(new RegisterGroup()));
using var provider = services.BuildServiceProvider();
provider.ValidateSynapse().ThrowIfInvalid(); // SYN003 for every request using SharedConnection
```

---

## Library version

`fix/223-async-disposable-scopes`

## .NET version

.NET 10.0

## Operating system

macOS

---

## Additional context

### Root cause

`PipelineDescriber`, `InMemoryOutboxProductionCheck`, `OutboxDispatcherService.WarnIfStorageCannotClaim` and
`SynapseMetrics.ResolveStorage` created scopes with `CreateScope()` and disposed them synchronously.
`ServiceProviderEngineScope.Dispose()` throws for a service that only implements `IAsyncDisposable`.

### Resolution

The two asynchronous sites use `CreateAsyncScope()` with `await using`. The synchronous ones (the public
`IPipelineDescriber` API and the metrics gauge callbacks) use `CreateAsyncScope()` and an internal
`DisposeSynchronously()` helper that completes `DisposeAsync()` synchronously; they run on a dedicated thread or
a meter-listener thread, without a synchronization context.

**Verification.** New tests in `PipelineDescriberTests`, `SynapseValidationStartupTests`,
`InMemoryOutboxProductionCheckTests`, `OutboxDispatcherServiceTests` and `SynapseMetricsTests` use a scoped
async-only service and fail without the fix; the service's `DisposeAsync` is asserted to run.
