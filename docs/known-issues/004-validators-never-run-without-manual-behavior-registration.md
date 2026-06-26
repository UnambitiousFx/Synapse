# [Bug]: `AddValidator` registers a validator that never runs

**Severity:** High
**Area:** Core DI / Validation
**Discovered on:** `feature/typed-pipeline-behaviors`, .NET 10
**Status:** ✅ Resolved

---

## Resolution

Both the preferred and the generator approach were implemented:

- **`AddValidator` now wires the behavior (Option A).** Both overloads delegate to a shared
  internal `IServiceCollection.RegisterValidator<…>` helper
  (`src/Synapse/DependencyInjectionExtensions.cs`) that registers the validator **and** the
  closed `RequestValidationBehavior<TRequest[, TResponse]>` as `IRequestPipelineBehavior`.
  Both registrations use `TryAddEnumerable`, so the behavior is wired **once per request** even
  when several validators target the same request (the behavior resolves
  `IEnumerable<IRequestValidator<TRequest>>` and runs them all in one pass).

- **New `[Validator]` source-generator path.** Decorating a validator with
  `[Validator]` (`src/Synapse.Abstractions/ValidatorAttribute.cs`) makes the generator emit a
  closed, Native-AOT-safe `builder.RegisterValidator<…>()` into `RegisterGroup` — no
  `AddValidator` call required. The request type is read from the implemented
  `IRequestValidator<TRequest>`; the response type is derived from
  `TRequest : IRequest<TResponse>`.

The example (`examples/MinimalApi`) now relies on `[Validator]` on `CreateTaskCommandValidator`
and the previous manual `AddValidator` + `RegisterRequestPipelineBehavior<RequestValidationBehavior<…>>`
lines were removed. Tests cover generator emission, behavior dedup, and end-to-end rejection
through `AddValidator` alone.

---

## Describe the bug

Calling `AddValidator<TValidator, TRequest, TResponse>()` registers the validator as
`IRequestValidator<TRequest>`, but **nothing registers `RequestValidationBehavior<,>` into the
pipeline**. After the switch from the marker-interface pipeline to closed-typed behavior
registration, validation is no longer wired up automatically.

Result: registering a validator silently does nothing. Invalid requests pass straight through to
the handler with no error and no compile failure.

---

## Steps to reproduce

1. Register a validator only (the documented surface):

   ```csharp
   builder.Services.AddSynapse(cfg =>
   {
       cfg.AddValidator<CreateTaskValidator, CreateTaskCommand, CreateTaskResult>();
   });
   ```

2. Dispatch a command that the validator should reject:

   ```csharp
   await invoker.InvokeAsync(new CreateTaskCommand(Title: ""), ct); // empty title = invalid
   ```

---

## Expected behavior

The validator runs, the request is rejected, and the handler never executes.

---

## Actual behavior

The validator never runs. The handler executes with invalid input.

---

## Root cause

- `AddValidator` registers only the validator
  (`src/Synapse/SynapseConfig.cs:341` / `:352`):

  ```csharp
  services.AddScoped<IRequestValidator<TRequest>, TValidator>();
  ```

- `RequestValidationBehavior<TRequest, TResponse>` is declared **without** a
  `[PipelineBehavior]` attribute (`src/Synapse/Pipelines/RequestValidationBehavior.cs:13`), so the
  source generator never discovers it, and no DI extension registers it as
  `IRequestPipelineBehavior<TRequest, TResponse>`.

- The only place validation currently runs is the example's **manual** registration
  (`examples/MinimalApi/Program.cs:102`):

  ```csharp
  cfg.RegisterRequestPipelineBehavior<RequestValidationBehavior<CreateTaskCommand, CreateTaskResult>,
      CreateTaskCommand, CreateTaskResult>();
  ```

Under the previous marker-interface design a single untyped `IRequestPipelineBehavior` picked up
the validation behavior for every request. The closed-typed redesign orphaned it.

---

## To address

- **Option A (preferred):** Have `AddValidator` also register the closed
  `RequestValidationBehavior<TRequest, TResponse>` as `IRequestPipelineBehavior<TRequest, TResponse>`
  (and the no-response variant), so registering a validator is sufficient.
- **Option B:** Mark `RequestValidationBehavior<,>` with `[PipelineBehavior]` so the generator
  emits the closed registration as an open-generic behavior, and document the ordering.
- Add a test asserting that `AddValidator` alone causes an invalid request to be rejected
  (current tests only exercise the behavior in isolation, masking the missing wiring).

## Library version

`feature/typed-pipeline-behaviors` (pre-release)

## .NET version

.NET 10.0
