# 010 — Per-endpoint validators

|  |  |
|---|---|
| **Status** | 🟡 Partial — works, but not colocated with the endpoint |
| **Priority** | Medium |
| **Area** | Validation |
| **Tiers** | All bound tiers |
| **Breaking** | No — additive |

## Problem

Binding validation and business validation live in two different worlds with two different response
shapes, and only one of them is near the endpoint.

- **Binding** (`BindingValidator`) is presence and parseability. Accumulates every problem, answers
  one `400` with a field-keyed `HttpValidationProblemDetails`. Lives next to the endpoint.
- **Business rules** go in an `IRequestValidator<TRequest>` plus a `RequestValidationBehavior`
  pipeline registration (`docs/docs/validation.mdx`). Lives in DI configuration, returns a
  `Result` failure that `IFailureHttpMapper` turns into whatever it turns it into — typically
  without the per-field error dictionary the binding path produces.

So a caller gets field-keyed errors for "id is not a guid" and an opaque problem document for "title
is too long". Same request, same endpoint, two error models.

## Current state

- `src/Synapse.Endpoints/Binding/BindingValidator.cs` — its own docs are explicit that business
  rules do not belong here: *"Its job is presence and parseability […] Business rules belong in
  `IRequestValidator` and the Synapse pipeline, not here."* Correct separation, wrong ergonomics.
- Registration is two calls per message (`AddValidator<…>` plus
  `RegisterRequestPipelineBehavior<RequestValidationBehavior<…>, …>`), neither near the endpoint.
  `docs/known-issues/004` records that forgetting the second one silently disables validation.
- No endpoint tier declares `422`, and `ProducesValidationProblem()` is emitted for the binding
  `400` only.

## What you cannot write today

The declaration you would reach for first:

```csharp
public override void Configure(IEndpointBuilder<TaskCreated> builder)
{
    // CS1061: 'IEndpointBuilder<TaskCreated>' does not contain a definition for 'Validator'
    builder.Validator<CreateTaskCommandValidator>()
           .Created(created => $"/tasks/{created.TaskId}");
}
```

So the endpoint's own validation rules are declared in `Program.cs`, in two calls that must both be
present:

```csharp
services.AddSynapse(cfg =>
{
    cfg.AddValidator<CreateTaskCommandValidator, CreateTaskCommand, TaskCreated>();

    // Forget this one and validation is silently off — the endpoint still compiles, still maps,
    // and still accepts everything. See docs/known-issues/004.
    cfg.RegisterRequestPipelineBehavior<
        RequestValidationBehavior<CreateTaskCommand, TaskCreated>,
        CreateTaskCommand,
        TaskCreated>();
});
```

And the two kinds of "bad request" answer in two different vocabularies. Same endpoint, same field:

```json title="POST /tasks, title sent as a number — binding"
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "title": ["The body value is not a valid string."] }
}
```

```json title="POST /tasks, title sent as an empty string — business rule"
{
  "status": 422,
  "detail": "Title is required."
}
```

The second shape is whatever the registered `IFailureHttpMapper` makes of the failure, and it cannot
carry field keys regardless: `Result.FailValidation(string message)` and `Result.Failure(string)`
both take one message, so there is no per-field dictionary to map. A client that wants to highlight
the offending input has to parse two error models and, for the second one, guess.

## Proposed API

Colocate the declaration without moving the execution:

```csharp
[Post("/tasks")]
public sealed class CreateTaskEndpoint : Endpoint<CreateTaskCommand, Guid>
{
    public override void Configure(IEndpointBuilder<Guid> builder)
    {
        builder.Validator<CreateTaskCommandValidator>()   // registers validator + behavior
               .Created(id => $"/tasks/{id}");
    }
}
```

and give validation failures the same wire shape as binding failures — a field-keyed
`HttpValidationProblemDetails`, so a client parses one error model rather than two. That likely
means a `ValidationFailure` type carrying field keys, recognised by `IFailureHttpMapper`.

### With the proposal

```csharp
[Post("/tasks")]
public sealed class CreateTaskEndpoint : Endpoint<CreateTaskCommand, TaskCreated>
{
    public override void Configure(IEndpointBuilder<TaskCreated> builder)
    {
        builder.Validator<CreateTaskCommandValidator>()   // validator + behaviour, one call
               .Created(created => $"/tasks/{created.TaskId}")
               .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
    }
}
```

with the validator returning field-keyed failures:

```csharp
public sealed class CreateTaskCommandValidator : IRequestValidator<CreateTaskCommand>
{
    public ValueTask<Result> ValidateAsync(CreateTaskCommand command, CancellationToken ct = default)
    {
        var v = ValidationFailures.Collect();
        v.Check(!string.IsNullOrWhiteSpace(command.Title), "title", "Title is required.");
        v.Check(command.Title.Length <= 200, "title", "Title must be 200 characters or fewer.");

        return ValueTask.FromResult(v.ToResult());
    }
}
```

so both failures arrive in one shape, and the only difference left is the status — which the
endpoint declared, rather than inheriting from whichever mapper happened to be registered:

```json title="POST /tasks, title sent as an empty string"
{
  "title": "One or more validation errors occurred.",
  "status": 422,
  "errors": { "title": ["Title is required."] }
}
```

`Program.cs` loses both registrations, and `docs/known-issues/004` cannot recur through this path
because there is only one call to forget.

## Acceptance criteria

- [ ] Declaring a validator on the endpoint registers both the validator and the behaviour, so
      `docs/known-issues/004` cannot recur through this path.
- [ ] Field-keyed validation failures round-trip to `HttpValidationProblemDetails`.
- [ ] The status code for business-rule failure (`400` vs `422`) is a documented, configurable
      decision — not an accident of the default mapper.
- [ ] The endpoint declares that status in OpenAPI (depends on [001](001-openapi-failure-responses.md)).
- [ ] Existing DI-side registration keeps working unchanged.

## Notes

Status is *partial*, not *missing*: validation works today and is documented. What is missing is
colocation and one consistent error contract.
