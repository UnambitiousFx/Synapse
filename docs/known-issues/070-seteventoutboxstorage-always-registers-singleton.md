# [Bug]: SetEventOutboxStorage always registers the storage as Singleton, breaking scoped dependencies

**Severity:** Medium
**Area:** Core DI
**Discovered on:** `worktree-ef-core-outbox`, .NET 10
**Status:** ✅ **Resolved** on `worktree-ef-core-outbox` — see [Resolution](#resolution).

> **TL;DR.** `ISynapseConfig.SetEventOutboxStorage<T>()` registered `T` as `IEventOutboxStorage` with
> `services.AddSingleton(...)` unconditionally, so any custom storage with a scoped dependency (most
> notably a `DbContext`) either threw at resolution time under validated scopes, or silently shared
> one instance across every request. `SetEventOutboxStorage<T>()` now takes an optional
> `ServiceLifetime` parameter, defaulting to `Scoped`.

---

## Describe the bug

`docs/docs/outbox.mdx`'s own "Replace the storage for production" section showed a sample
`EfCoreEventOutboxStorage` constructed with a `DbContext`. Following that sample and registering it
via `cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage>()` produced a storage that was silently
unsafe (or outright broken under `ServiceProviderOptions.ValidateScopes = true`), because the
registration path always used `services.AddSingleton(...)` regardless of what the configured type's
own dependencies were.

---

## Steps to reproduce

1. Implement a custom `IEventOutboxStorage` that takes a `DbContext` (or any other scoped service) in
   its constructor.
2. Register it: `services.AddSynapse(cfg => cfg.SetEventOutboxStorage<MyStorage>());`.
3. Build the provider with `ServiceProviderOptions.ValidateScopes = true` and resolve
   `IEventOutboxStorage` from a scope.

---

## Expected behavior

The storage resolves per-scope, matching the lifetime of the `DbContext` (or other scoped
dependency) it depends on.

---

## Actual behavior

`InvalidOperationException: Cannot consume scoped service ... from singleton ...` when scope
validation is enabled; without validation, one instance — and its captured `DbContext` — is silently
shared across every request for the lifetime of the process, which is unsafe (`DbContext` is not
thread-safe) and defeats the point of enlisting in a per-request transaction.

---

## Code sample

```csharp
services.AddSynapse(cfg => cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage<AppDbContext>>());
var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
using var scope = provider.CreateScope();
scope.ServiceProvider.GetRequiredService<IEventOutboxStorage>(); // throws
```

---

## Library version

`worktree-ef-core-outbox` (pre-release)

## .NET version

.NET 8 / 9 / 10

## Operating system

Any

---

## Additional context

### Root cause

`SynapseConfig.Apply()` called `services.AddSingleton(typeof(IEventOutboxStorage), _eventOutBoxStorage);`
unconditionally in `src/Synapse/SynapseConfig.cs`, regardless of whether `_eventOutBoxStorage` was the
default `InMemoryEventOutboxStorage` (correctly Singleton — it is deliberately shared, in-memory,
stateful across the whole process) or a caller-supplied type via `SetEventOutboxStorage<T>()`, whose
correct lifetime depends entirely on what `T` itself depends on.

### Resolution

`ISynapseConfig.SetEventOutboxStorage<TEventOutboxStorage>()` now takes an optional
`ServiceLifetime lifetime = ServiceLifetime.Scoped` parameter. `SynapseConfig` registers via
`services.Add(new ServiceDescriptor(typeof(IEventOutboxStorage), _eventOutBoxStorage, _eventOutBoxStorageLifetime))`,
where `_eventOutBoxStorageLifetime` defaults to `ServiceLifetime.Singleton` and is only overwritten
when `SetEventOutboxStorage<T>()` is actually called — so the untouched default path
(`InMemoryEventOutboxStorage`, nobody calling `SetEventOutboxStorage`) keeps its exact original
Singleton registration. A caller invoking `SetEventOutboxStorage<T>()` with no argument now gets
`Scoped`, correct for a `DbContext`-backed storage such as
`UnambitiousFx.Synapse.Outbox.EntityFrameworkCore`'s `EfCoreEventOutboxStorage<TContext>`; a caller
that needs `Singleton` for a stateless, thread-safe storage passes it explicitly.

**Verification.** `test/Synapse.Tests/SynapseConfigEventOutboxStorageLifetimeTests.cs` — asserts the
default path stays `InMemoryEventOutboxStorage`/`Singleton`, a no-argument `SetEventOutboxStorage<T>()`
registers `Scoped`, an explicit `ServiceLifetime.Singleton` is honored, and a scoped custom storage
resolves cleanly under `ValidateScopes = true`.
