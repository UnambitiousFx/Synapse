# [Bug]: Open-generic pipeline behavior registrations throw at runtime under Native AOT when `TResponse` is a value type

**Severity:** High  
**Area:** Core DI (`src/Synapse`)  
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10, `PublishAot=true`

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

**Current workaround used in `examples/MinimalApi`:** wrap the value type in a class record:

```csharp
// Instead of IRequest<Guid>:
public sealed record CreateTaskResult { public required Guid TaskId { get; init; } }
public sealed record CreateTaskCommand : IRequest<CreateTaskResult> { ... }
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

### Suggested fixes (to address)

**Option A (preferred) — source-generator closed registrations:**  
Extend `Synapse.Generator` to emit closed `IRequestPipelineBehavior<TRequest, TResponse>`
registrations for `CqrsBoundaryEnforcementBehavior` for each discovered request type, exactly as
it already does for `[PipelineBehavior]`-attributed behaviors in `RegisterGroup`. This eliminates
the open-generic descriptor entirely and is fully AOT-safe.

**Option B — `[RequiresDynamicCode]` annotation:**  
Annotate `EnableCqrsBoundaryEnforcement()` and `AddOpenGenericRequestWithResponsePipelineBehavior()`
with `[RequiresDynamicCode("...")]`. This causes the compiler/trimmer to warn AOT consumers at
publish time. Does not fix the runtime error but makes it diagnosable earlier.

**Option C — document as known limitation:**  
Document that AOT projects must use class response types (no `IRequest<ValueType>`) when
open-generic behaviors are registered. Lowest effort; least user-friendly.

> See also: [002](002-validateonbuild-does-not-suppress-aot-open-generic-check.md) for a
> related misunderstanding about `ValidateOnBuild`.
