# [Bug]: IEventOutboxStorage and the concrete outbox storage type resolve to independent instances

**Severity:** Medium
**Area:** Core DI
**Discovered on:** `main`, .NET 8/9/10
**Status:** ✅ **Resolved** on `fix/outbox-storage-forwarding` — see [Resolution](#resolution).

> **TL;DR.** `SetEventOutboxStorage<T>()` registered `IEventOutboxStorage` as an independent
> `ServiceDescriptor` for `T`, so a caller that also registered `T` as its own service (e.g.
> `AddEfCoreEventOutbox<TContext>()`) got two separate instances of `T` per scope instead of one.

---

## Describe the bug

`AddEfCoreEventOutbox<TContext>()` registers `EfCoreEventOutboxStorage<TContext>` as its own
concrete-type service. `SetEventOutboxStorage<EfCoreEventOutboxStorage<TContext>>()` separately adds
a `ServiceDescriptor(typeof(IEventOutboxStorage), typeof(EfCoreEventOutboxStorage<TContext>), lifetime)`.
The container treats these as two unrelated registrations of the same implementation type and
activates each independently, so one scope ends up holding two separate `EfCoreEventOutboxStorage<TContext>`
instances.

## Steps to reproduce

1. `services.AddEfCoreEventOutbox<AppDbContext>();`
2. `services.AddSynapse(cfg => cfg.SetEventOutboxStorage<EfCoreEventOutboxStorage<AppDbContext>>());`
3. In one scope, resolve both `IEventOutboxStorage` and `EfCoreEventOutboxStorage<AppDbContext>` directly.

## Expected behavior

Both resolutions return the same instance within a scope.

## Actual behavior

Two distinct instances are returned. Each keeps its own `_storedByReference` map used by
`DiscardAsync`, so a directly-injected concrete-type instance never sees events recorded through
`IEventOutboxStorage` — every `DiscardAsync` call through it silently no-ops.

## Code sample

```csharp
using var scope = provider.CreateScope();
var viaInterface = scope.ServiceProvider.GetRequiredService<IEventOutboxStorage>();
var viaConcreteType = scope.ServiceProvider.GetRequiredService<EfCoreEventOutboxStorage<AppDbContext>>();

Assert.Same(viaConcreteType, viaInterface); // failed before the fix
```

---

## Library version

`main` (pre-fix)

## .NET version

.NET 8.0 / 9.0 / 10.0

## Operating system

macOS

---

## Additional context

### Root cause

`SynapseConfig.Apply()` always built `IEventOutboxStorage`'s `ServiceDescriptor` by implementation
type, regardless of whether that implementation type was already registered as its own service
elsewhere in the collection. Two `ServiceDescriptor`s naming the same implementation type are
activated independently by the container — registering by implementation type is not "reuse an
existing registration," it constructs a brand-new one.

### Resolution

`Apply()` now looks for an existing `ServiceDescriptor` whose `ServiceType` matches the configured
storage type. If found (e.g. because `AddEfCoreEventOutbox<TContext>()` ran first), `IEventOutboxStorage`
is registered with a factory that forwards to `sp.GetRequiredService(storageType)` — reusing that
registration's lifetime — so both resolve to the same instance per scope. If no such registration
exists (the default `InMemoryEventOutboxStorage` path, or a custom storage type that isn't
separately self-registered), the original by-implementation-type registration is used unchanged.

This only forwards correctly when the concrete-type registration (e.g. `AddEfCoreEventOutbox`) runs
**before** `AddSynapse`, which is the order the docs already show.

**Verification.** Added
`EfCoreOutboxServiceRegistrationTests.DocumentedRegistration_IEventOutboxStorageAndConcreteTypeShareOneInstancePerScope`,
confirmed it failed (`Assert.Same` mismatch) before the fix and passes after. Full
`Synapse.Outbox.EntityFrameworkCore.Tests` (75 tests) and `Synapse.Tests` (1143 tests) suites pass.
