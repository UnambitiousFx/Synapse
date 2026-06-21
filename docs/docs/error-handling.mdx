---
sidebar_position: 9
---

# Error Handling

Synapse embraces the principle that **errors are values**. Handlers return `Result` / `Result<T>` from [UnambitiousFx.Functional](/lib-functional) rather than throwing exceptions for domain errors. Exceptions are reserved for genuine programming errors (misconfiguration, missing handlers).

## Result at the call site

Every `IInvoker.InvokeAsync` call returns a `Result` or `Result<T>`. Use whichever consumption pattern fits the context:

### `TryGet` — out-variable style

```csharp
var result = await invoker.InvokeAsync(new CreateTaskCommand("Buy milk"), ct);

if (result.TryGet(out var id, out var error))
    return Results.Created($"/tasks/{id}", id);
else
    return Results.Problem(error.ToString());
```

### `Match` — functional style

```csharp
return result.Match(
    success: id    => Results.Created($"/tasks/{id}", id),
    failure: error => Results.Problem(error.ToString()));
```

### `IsSuccess` guard

```csharp
if (!result.IsSuccess)
{
    logger.LogError("Create task failed: {Error}", result.Error);
    return;
}
Process(result.Value);
```

## Returning failures from handlers

Return a failure directly from a handler — do not throw:

```csharp
public async ValueTask<Result<Guid>> HandleAsync(CreateTaskCommand cmd, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(cmd.Title))
        return Result.Failure<Guid>("Title is required.");   // ← return, not throw

    var id = Guid.NewGuid();
    await _repo.SaveAsync(id, cmd.Title, ct);
    return Result.Success(id);
}
```

## Combining multiple results

Combine a collection of `Result` / `Result<T>` values into a single result. It succeeds only when all inputs succeed; otherwise all failures are aggregated:

```csharp
var results = new[]
{
    validator1.ValidateAsync(cmd),
    validator2.ValidateAsync(cmd),
};

var combined = results.Combine();
if (!combined.IsSuccess)
    return combined; // all individual failures merged
```

## Propagation through pipelines

Failures propagate transparently through the pipeline without any special handling:

- A behavior that calls `next()` and gets back `Result.Failure(...)` can inspect it, transform it, or return it unchanged.
- A behavior can short-circuit by returning `Result.Failure(...)` without calling `next()` at all (e.g., a validation behavior).

```csharp
public async ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(
    TRequest request,
    RequestHandlerDelegate<TResponse> next,
    CancellationToken ct)
{
    // Short-circuit example
    if (!IsAllowed(request))
        return Result.Failure<TResponse>("Access denied.");

    return await next();
}
```

## Exceptions

Exceptions are thrown only for programming errors — situations that indicate misconfiguration, not expected domain failures:

| Exception                        | When thrown                                                                                 |
| -------------------------------- | ------------------------------------------------------------------------------------------- |
| `MissingHandlerException`        | No handler registered for the request type. This is a startup/configuration error.          |
| `CqrsBoundaryViolationException` | A command was dispatched from inside a query (or vice versa) with CQRS enforcement enabled. |
| `MissingContextFeatureException` | `IContext.GetRequiredFeature<T>()` called but the feature was never set.                    |

These should never appear in normal application flow. If they do, fix the configuration rather than catching them.

## See also

- [UnambitiousFx.Functional documentation](/lib-functional/result/) — full `Result<T>` API: `Bind`, `Map`, `Ensure`, `Recover`, LINQ support.
- [Validation](./validation) — validation failures surface as `Result.Failure` before the handler runs.
- [Pipeline Behaviors](./pipelines) — behaviors can inspect and transform failures.
