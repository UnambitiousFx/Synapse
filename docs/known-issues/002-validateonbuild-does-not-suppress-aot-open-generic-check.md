# [Bug]: `ValidateOnBuild = false` does not suppress the AOT open-generic runtime check

**Severity:** Medium  
**Area:** Core DI / Documentation  
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10, `PublishAot=true`  
**Depends on:** [001](001-open-generic-pipeline-behavior-aot-value-type.md)

---

## Describe the bug

When issue [001](001-open-generic-pipeline-behavior-aot-value-type.md) is present (open-generic
`IRequestPipelineBehavior<,>` + value-type `TResponse` in an AOT project), the natural first
workaround is to disable the service-provider's build-time validation:

```csharp
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateOnBuild = false;
    o.ValidateScopes  = builder.Environment.IsDevelopment();
});
```

This silences the **startup** `AggregateException` but the **same
`InvalidOperationException`** is thrown again on the first HTTP request that triggers service
resolution. The workaround therefore gives false confidence — the app appears to start, but every
affected endpoint fails at runtime.

---

## Steps to reproduce

1. Reproduce issue [001] (AOT project, `IRequest<Guid>` handler, `EnableCqrsBoundaryEnforcement()`).
2. Add `UseDefaultServiceProvider(o => o.ValidateOnBuild = false)` **before** `builder.Build()`.
3. Confirm `dotnet run` now starts without an `AggregateException`.
4. `POST` to the affected endpoint.

---

## Expected behavior

`ValidateOnBuild = false` suppresses validation errors for this registration pattern, and the
endpoint works (or the developer receives a clear compile-time/publish-time warning instead of a
silent runtime crash).

---

## Actual behavior

The startup `AggregateException` disappears, but the runtime request throws:

```
System.InvalidOperationException: Unable to create a generic service for type
'IRequestPipelineBehavior`2[CreateTaskCommand,System.Guid]' because 'System.Guid' is a ValueType.
   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteFactory.VerifyOpenGenericAotCompatibility(...)
   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteFactory.TryCreateEnumerable(...)
   at UnambitiousFx.Synapse.Resolvers.DefaultDependencyResolver.GetRequiredService[TService]()
```

The error is identical to the one reported in issue 001 — just moved from startup to the first
request.

---

## Code sample

```csharp
// ❌ Misleading workaround — silences startup but NOT runtime resolution
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateOnBuild = false;                               // suppresses startup AggregateException only
    o.ValidateScopes  = builder.Environment.IsDevelopment(); // unrelated
});

builder.Services.AddSynapse(cfg =>
{
    cfg.RegisterRequestHandler<CreateTaskCommandHandler, CreateTaskCommand, Guid>();
    cfg.EnableCqrsBoundaryEnforcement(); // still registers open-generic IRequestPipelineBehavior<,>
});

// POST /tasks → still throws InvalidOperationException at runtime
```

---

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0

## Operating system

Windows 11

---

## Additional context

### Why `ValidateOnBuild = false` is not enough

`ServiceProviderOptions.ValidateOnBuild` controls whether `ServiceProvider.Build()` eagerly
iterates all registered descriptors and calls `GetCallSite` on each one. Setting it to `false`
skips this sweep.

However, `CallSiteFactory.VerifyOpenGenericAotCompatibility` is **also called during normal
per-request resolution** (`TryCreateEnumerable` → `CreateOpenGeneric`) whenever an
`IEnumerable<IRequestPipelineBehavior<TRequest, TResponse>>` is resolved. This path runs
regardless of `ValidateOnBuild` because it is gated only on
`CallSiteFactory.RequiresDynamicCode`, which is set to `true` when
`!RuntimeFeature.IsDynamicCodeSupported` — a flag that is `false` in any project compiled with
`PublishAot=true`, even in JIT `dotnet run` mode.

In short: `ValidateOnBuild` and the AOT open-generic check are **two separate mechanisms**;
disabling the former does not affect the latter.

### To address

1. **Remove the misleading workaround** from `examples/MinimalApi/Program.cs` — it has already
   been removed after this was understood; ensure it does not reappear in documentation or
   samples.
2. **Fix issue [001]** at the root — once `CqrsBoundaryEnforcementBehavior` no longer registers
   as an open generic, `ValidateOnBuild` becomes irrelevant to this scenario.
3. **Add a note** to the AOT documentation section explaining that `ValidateOnBuild` governs
   startup sweeps only and that AOT type-instantiation rules are enforced separately at runtime.
