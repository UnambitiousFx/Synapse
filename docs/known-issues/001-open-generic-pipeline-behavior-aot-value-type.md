# [Bug]: Open-generic pipeline behavior registrations throw at runtime under Native AOT when `TResponse` is a value type

**Severity:** High  
**Area:** Core DI (`src/Synapse`)  
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10, `PublishAot=true`  
**Status:** ✅ **Resolved** on `feature/typed-pipeline-behaviors` — see [Resolution](#resolution).

> **TL;DR.** CQRS boundary enforcement is now opted-in via the assembly attribute
> `[assembly: EnableSynapseCqrsBoundaryEnforcement]`; the source generator emits **closed**
> `CqrsBoundaryEnforcementBehavior<TRequest, TResponse>` registrations (one per request handler,
> inserted at the front of the pipeline). User behaviors should use `[PipelineBehavior]` for the same
> closed-registration treatment. Value-type responses (`IRequest<Guid>`, `IRequest<int>`) now work
> under Native AOT — the class-wrapper workaround is no longer required.

---

## Describe the bug

Any open-generic `IRequestPipelineBehavior<TRequest, TResponse>` registration throws an
`InvalidOperationException` at **request-resolution time** (not at startup or publish time) when
the concrete `TResponse` is a value type (`Guid`, `int`, `bool`, etc.).

This affects:

- **`cfg.EnableCqrsBoundaryEnforcement()`** — registers `CqrsBoundaryEnforcementBehavior<,>` as
  an unconstrained open generic (`src/Synapse/SynapseConfig.cs:327–335`).
- **`cfg.AddOpenGenericRequestWithResponsePipelineBehavior(typeof(MyBehavior<,>))`** — registers
  any user-supplied open-generic behavior (`src/Synapse/SynapseConfig.cs:85–113`).

Because `EnableCqrsBoundaryEnforcement()` is part of the standard recommended setup and is
completely unconstrained, **every** `IRequest<TValueType>` handler in an AOT project is broken by
default.

---

## Steps to reproduce

1. Create an ASP.NET Core Minimal API project with `<PublishAot>true</PublishAot>`.
2. Register Synapse with CQRS enforcement:

   ```csharp
   builder.Services.AddSynapse(cfg =>
   {
       cfg.RegisterRequestHandler<CreateItemHandler, CreateItemCommand, Guid>();
       cfg.EnableCqrsBoundaryEnforcement();
   });
   ```

3. Define a command that returns a value type:

   ```csharp
   public sealed record CreateItemCommand : IRequest<Guid> { ... }
   ```

4. Run the app with `dotnet run` and invoke `POST /items`.

---

## Expected behavior

The request pipeline resolves, `CqrsBoundaryEnforcementBehavior<CreateItemCommand, Guid>` is
instantiated, and the handler executes normally.

---

## Actual behavior

The request throws at resolution time:

```
System.InvalidOperationException: Unable to create a generic service for type
'UnambitiousFx.Synapse.Abstractions.IRequestPipelineBehavior`2
[CreateItemCommand,System.Guid]' because 'System.Guid' is a ValueType.
Native code to support creating generic services might not be available with native AOT.
   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteFactory.VerifyOpenGenericAotCompatibility(...)
   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteFactory.CreateOpenGeneric(...)
   at Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteFactory.TryCreateEnumerable(...)
   at UnambitiousFx.Synapse.Resolvers.DefaultDependencyResolver.GetRequiredService[TService]()
   at UnambitiousFx.Synapse.Invoker.InvokeAsync[TResponse](...)
```

The app starts successfully — the error only surfaces on the first request that closes the
open-generic with a value-type argument.

---

## Code sample

```csharp
// Minimal repro — AOT project, PublishAot=true

public sealed record CreateItemCommand : IRequest<Guid>;  // Guid is a ValueType ← root cause

[RequestHandler<CreateItemCommand, Guid>]
public sealed class CreateItemHandler : IRequestHandler<CreateItemCommand, Guid>
{
    public ValueTask<Result<Guid>> HandleAsync(CreateItemCommand request, CancellationToken ct)
        => ValueTask.FromResult(Result.Success(Guid.NewGuid()));
}

// In Program.cs:
builder.Services.AddSynapse(cfg =>
{
    cfg.RegisterRequestHandler<CreateItemHandler, CreateItemCommand, Guid>();
    cfg.EnableCqrsBoundaryEnforcement();  // registers IRequestPipelineBehavior<,> as open generic
});
```

**Former workaround (no longer needed):** wrap the value type in a class record so the response type is
a reference type. After the fix below, `examples/MinimalApi` instead uses
`PurgeCompletedTasksCommand : IRequest<int>` directly to exercise the value-type path under AOT.

---

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0

## Operating system

Windows 11

---

## Additional context

### Root cause

MS DI's `CallSiteFactory.VerifyOpenGenericAotCompatibility` rejects open-generic service
descriptors whose type arguments resolve to value types. This check runs on every service
resolution (not just startup) when `PublishAot=true` is set in the consuming project's csproj,
because the project is compiled with `RuntimeFeature.IsDynamicCodeSupported = false`.

The relevant library code:

- `src/Synapse/SynapseConfig.cs:327–335` — `EnableCqrsBoundaryEnforcement()` inserts two
  open-generic `ServiceDescriptor`s.
- `src/Synapse/SynapseConfig.cs:106–113` — `AddOpenGenericBehavior()` is the shared helper used
  by all `AddOpenGeneric*PipelineBehavior()` methods.
- `src/Synapse/Resolvers/DefaultDependencyResolver.cs:21–25` — where the resolution throws.

### Resolution

The fix combines Option A (generator-emitted closed registrations) for the library's CQRS behavior with
the existing `[PipelineBehavior]` path for user behaviors, plus Option B (`[RequiresDynamicCode]`) as a
guard-rail on the remaining runtime open-generic APIs.

**1. CQRS enforcement is now opted-in via an assembly attribute.**
A runtime fluent call is invisible to the source generator, so enforcement is expressed as
`[assembly: EnableSynapseCqrsBoundaryEnforcement]`
(`src/Synapse.Abstractions/EnableSynapseCqrsBoundaryEnforcementAttribute.cs`). The generator detects it
(`CompilationExtensions.IsCqrsBoundaryEnforcementEnabled`) and, for each discovered request handler, emits
a **closed** registration:

```csharp
// Generated in RegisterGroup.g.cs — closed over the concrete (value-type) response, AOT-safe:
builder.RegisterCqrsBoundaryEnforcement<PurgeCompletedTasksCommand, int>();
```

`CqrsBoundaryEnforcementBehavior` implements `IOrderedPipelineBehavior` with
`IOrderedPipelineBehavior.First`, so it stays **outermost** at runtime regardless of registration order
— preserving the previous guarantee without an open-generic descriptor. (Originally this used a
front-insertion helper; see issue
[009](009-behavior-order-not-honored-across-registration-sources.md) for why ordering moved to the
runtime interface.)

`cfg.EnableCqrsBoundaryEnforcement()` is now `[Obsolete]` and a no-op (registering it alongside the
generated closed registrations would double-wrap the pipeline and make `CqrsBoundaryMetadata.Validate()`
throw on every request).

**2. User open-generic behaviors use `[PipelineBehavior]`.**
The generator already cross-products `[PipelineBehavior]`-attributed open generics into closed
registrations (honouring generic constraints). `examples/MinimalApi`'s `AuthorizationBehavior<,>` now
carries `[PipelineBehavior]` instead of being registered via `cfg.AddOpenGenericRequestWithResponsePipelineBehavior(...)`.

**3. Runtime open-generic APIs that can close over a value type are annotated.**
`AddOpenGenericRequestWithResponsePipelineBehavior` and `AddOpenGenericStreamRequestPipelineBehavior` are
marked `[RequiresDynamicCode(...)]`, so AOT consumers get a publish-time IL3050 warning pointing them to the
`[PipelineBehavior]` / closed-registration path instead of a runtime crash. (The request-only and event-only
open-generic overloads close over reference types and remain un-annotated.)

**Verification.** `examples/MinimalApi` publishes with `PublishAot=true` and no IL3050/trim warnings;
`POST /tasks/admin/purge` (response type `int`) returns `200` instead of throwing
`InvalidOperationException … 'System.Int32' is a ValueType`. Covered by
`test/Synapse.Generator.Tests/GeneratorBehaviorTests.cs`
(`CqrsEnforcement_WhenAssemblyOptIn_EmitsClosedRegistrationForValueTypeResponse`) and the runtime
`CqrsBoundaryEnforcementTests`.

> See also: [002](002-validateonbuild-does-not-suppress-aot-open-generic-check.md) for a
> related misunderstanding about `ValidateOnBuild` (now moot for the CQRS path).
