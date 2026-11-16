# [Bug]: ValidateOnStart() can deadlock host start-up under Native AOT

**Severity:** High
**Area:** Core DI
**Discovered on:** issue [#221](https://github.com/UnambitiousFx/Synapse/issues/221), Synapse 3.3.0, .NET 10 Native AOT, linux-musl-arm64
**Status:** ✅ **Resolved** on `fix/221-validate-on-start-deadlock` — see [Resolution](#resolution).

> **TL;DR.** The start-up validation ran on the thread that starts the host, where the DI container could run
> out of stack, hand the resolution to another thread while holding its lock, and deadlock. It now runs on a
> dedicated thread with a 16 MB stack.

---

## Describe the bug

With `ValidateOnStart()`, host start-up intermittently never completed in a Native AOT app on linux-musl-arm64:
`SynapseValidationStartup.StartAsync` never returned, so Kestrel never started listening. The process used no
CPU and every thread was parked in `futex_wait`.

---

## Steps to reproduce

1. Publish an ASP.NET Core minimal API with `PublishAot=true` for `linux-musl-arm64` and run it on
   `mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine`.
2. Register Synapse with a register group, an outbox dispatcher and `ValidateOnStart()`, with about 15
   request / event handlers, some of them depending on services registered with factories.
3. Start 12 containers of the image at the same time and wait for the liveness endpoint.

---

## Expected behavior

Every container starts, and `ValidateOnStart()` either passes or fails fast.

---

## Actual behavior

7 of 12 simultaneous starts hung forever. Without `ValidateOnStart()`, 0 of 37 starts hung. Calling
`ValidateSynapse()` right after `Build()` in the native binary also hung intermittently; under the JIT it
never did.

---

## Code sample

```csharp
services.AddSynapse(cfg => cfg
    .AddRegisterGroup(new RegisterGroup())
    .AddOutboxDispatcher(o => o.PollingInterval = TimeSpan.FromSeconds(2))
    .ValidateOnStart());   // removing this line made the hang disappear
```

---

## Library version

UnambitiousFx.Synapse 3.3.0

## .NET version

.NET 10.0 (ILCompiler 10.0.15), Native AOT

## Operating system

Alpine Linux (musl), arm64

---

## Additional context

### Root cause

The validation resolves every pipeline (`SynapseValidator.Validate` → `PipelineDescriber.Describe<…>` →
`GetService`). Microsoft.Extensions.DependencyInjection guards its recursive call-site resolver with a
`StackGuard`: when `RuntimeHelpers.TryEnsureSufficientExecutionStack()` fails, it continues the resolution on a
thread-pool thread and blocks waiting for it. The waiting thread still holds the scope's resolution lock. The
hand-off only knows about that lock through the resolution context, so when a service factory on the new thread
calls `GetRequiredService` (a fresh resolution with a fresh context), it calls `Monitor.Enter` on the same lock and
waits for the blocked thread forever. Synapse's own `IContext` → `IContextAccessor` → `ContextHandler` forwarding
factories are of that shape, as are many applications' registrations. The deadlock itself is in the container
(dotnet/runtime); Synapse triggered it by resolving every pipeline on whatever thread starts the host, which under
Native AOT on musl had too little usable stack.

### Resolution

`SynapseValidationStartup.StartAsync` now runs `ValidateSynapse()` on a dedicated background thread created with
a 16 MB stack (`Synapse start-up validation`), so the stack guard has room and does not hop. Thread-pool threads
were not used: their stacks are smaller than the main thread's, which makes the hop more likely. The report or
exception is handed back through a `TaskCompletionSource`; warnings are logged and `ThrowIfInvalid()` throws the
same `SynapseValidationException` as before. The wait honors the start-up cancellation token. The XML docs of
`ValidateOnStart()` and `ValidateSynapse()` now describe the thread and the Native AOT risk of calling
`ValidateSynapse()` from a small-stack thread.

**Verification.** `SynapseValidationStartupTests` (invalid setup still throws `SynapseValidationException`,
warnings still logged, validation resolves pipelines on a different thread with the expected name, cancellation
before completion throws `OperationCanceledException`); the full `Synapse.Tests` project; a Native AOT
`osx-arm64` publish of `examples/MinimalApi`, which uses `ValidateOnStart()`, starting and serving requests. The
deadlock itself depends on stack sizes and timing and is not reproduced in CI.
