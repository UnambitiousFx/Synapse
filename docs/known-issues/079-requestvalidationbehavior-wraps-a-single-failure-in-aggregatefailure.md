# [Bug]: RequestValidationBehavior wraps a single validator failure in AggregateFailure

**Severity:** Medium
**Area:** Pipeline / CQRS
**Discovered on:** `main` (reported as issue #231), .NET 11
**Status:** ✅ **Resolved** on `fix/231-validation-single-failure` — see [Resolution](#resolution).

> **TL;DR.** `RequestValidationBehavior<TRequest>` and `RequestValidationBehavior<TRequest, TResponse>`
> combined validator results with `ResultExtensions.Combine()`, which always returns an
> `AggregateFailure` once any result fails — even when only one validator ran and failed. Callers lost
> the validator's own failure type, code, message and metadata behind `AGGREGATE_ERROR`. The behaviors
> now run the validators directly: a single failure is returned unchanged, several `ValidationFailure`s
> are merged into one with every `ValidationError` preserved, and only a genuine mix of failure types
> still falls back to `AggregateFailure`.

---

## Describe the bug

`RequestValidationBehavior<TRequest,TResponse>` and `RequestValidationBehavior<TRequest>` combine the
validators' results with `Combine()` (`UnambitiousFx.Functional`). `Combine()` returns an
`AggregateFailure` as soon as one result fails, even when only one validator ran and failed. The
caller then gets code `AGGREGATE_ERROR`, message "Multiple errors occurred" and no metadata; the
validator's own failure (its type, code, message, metadata such as field paths) is only reachable
through `AggregateFailure.Errors`.

Anything that maps failures to responses by code or type (an HTTP problem mapper, an MCP tool answer,
logs) therefore loses the validator's code and field errors.

---

## Steps to reproduce

1. Declare one validator with `[Validator]` for a command.
2. Make it return `Result.Failure(new ValidationFailure([new ValidationError("name", "Name is required.", "orders.name_required")]))`.
3. Send the command.

---

## Expected behavior

- When exactly one validator fails, the pipeline returns that failure unchanged (same type, code,
  message and metadata).
- When several fail, the pipeline returns one failure that keeps every error's field path and code,
  for example one `ValidationFailure` merging their `ValidationError`s (as
  `UnambitiousFx.Synapse.FluentValidation` already does), or a flattening of them.

## Actual behavior

The result is an `AggregateFailure` with code `AGGREGATE_ERROR`, whatever the validator returned.

---

## Code sample

```csharp
public sealed record CreateOrder(string Name) : ICommand;

[Validator]
public sealed class CreateOrderValidator : IRequestValidator<CreateOrder>
{
    public ValueTask<Result> ValidateAsync(CreateOrder request, CancellationToken ct)
        => ValueTask.FromResult(string.IsNullOrWhiteSpace(request.Name)
            ? Result.Failure(new ValidationFailure([new ValidationError("name", "Name is required.", "orders.name_required")]))
            : Result.Success());
}

// var result = await invoker.InvokeAsync(new CreateOrder(""), ct);
// result's failure: AggregateFailure, Code == "AGGREGATE_ERROR" (expected: the ValidationFailure above)
```

---

## Library version

3.6.0 (same code on `main`: `src/Synapse/Pipelines/RequestValidationBehavior.cs`)

## .NET version

.NET 11

## Operating system

macOS / Linux / Windows

---

## Additional context

A related detail raised in the issue: the built-in validation behavior defaults to
`IOrderedPipelineBehavior.Last`, same as `UnambitiousFx.Synapse.FluentValidation`'s
`FluentValidationBehaviorOptions.Order` — both already agree, and this codebase has no
`PipelineStage` concept or `ConfigureBehaviorOrdering` extension today, so that part of the issue
describes a feature that does not exist yet and is out of scope for this fix.

### Root cause

`ResultExtensions.Combine(IEnumerable<Result>)` in `UnambitiousFx.Functional` is documented to
aggregate "if any result failed" — it has no special case for a single failure. Both
`RequestValidationBehavior<TRequest>.HandleAsync` and `RequestValidationBehavior<TRequest,TResponse>.HandleAsync`
ran every validator through `.Select(x => x.ValidateAsync(...)).Combine()`, so one failing validator
out of N was already enough to produce an `AggregateFailure` wrapping that single failure.

### Resolution

Both behaviors now go through a shared `RequestValidationRunner.ValidateAsync()` helper
(`src/Synapse/Pipelines/RequestValidationBehavior.cs`) that runs the validators itself instead of
calling `Combine()`:

```csharp
if (failures.Count == 1)
{
    return failures[0];
}

return failures.All(failure => failure is ValidationFailure)
    ? MergeValidationFailures(failures)
    : new AggregateFailure(failures);
```

- Zero failures → `null` (success).
- Exactly one failure → returned unchanged, same type/code/message/metadata as the validator produced.
- Several failures, all `ValidationFailure` → merged into one `ValidationFailure` whose `Errors` is the
  concatenation of every validator's `ValidationError`s (and `Extra` dictionaries merged), mirroring
  `UnambitiousFx.Synapse.FluentValidation`'s `FluentValidationRunner`.
- Several failures of mixed types → still an `AggregateFailure`, since there is no lossless way to
  merge arbitrary failure types; `AggregateFailure.Errors` keeps every original failure reachable.

**Verification.** Added `RequestValidationBehaviorTests` cases for: a single failure returned
unchanged (`Assert.Same`), multiple `ValidationFailure`s merged into one with both field errors
present, and a mix of a `ValidationFailure` and a plain `Failure` still producing an `AggregateFailure`
with both originals in `Errors` — covering both the void and response-returning behavior. The full
`test/Synapse.Tests` suite passes (1608/1608 across net8.0/net9.0/net10.0/net11.0), and
`dotnet build src/Synapse/Synapse.csproj` is 0 warnings, 0 errors.
