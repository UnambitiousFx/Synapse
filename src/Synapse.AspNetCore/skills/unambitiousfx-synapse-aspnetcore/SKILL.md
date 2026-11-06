---
name: unambitiousfx-synapse-aspnetcore
description: Expose UnambitiousFx.Synapse commands and queries over HTTP with IHttpInvoker (Minimal API) or IMvcInvoker (controllers), map failures to ProblemDetails, and propagate trace context. Use when a project references UnambitiousFx.Synapse.AspNetCore or when writing endpoints/controllers that dispatch Synapse requests.
---

# UnambitiousFx.Synapse.AspNetCore

Turns a Synapse `Result`/`Result<T>` into an HTTP response, so endpoints don't `Match` by hand.

## Setup

```csharp
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.AspNetCore;

builder.Services.AddSynapse(cfg => cfg.AddRegisterGroup(new RegisterGroup()));
builder.Services.AddSynapseAspNetCore();

var app = builder.Build();
app.UseSynapsePropagation();   // early, before endpoints: traceparent in, Trace-Id/traceresponse out
```

Internet-facing app: `app.UseSynapsePropagation(o => o.TrustIncomingHeader = false);` so callers can't choose the trace id or inject baggage.

## Minimal API — `IHttpInvoker` (`UnambitiousFx.Synapse.AspNetCore.Http`)

```csharp
app.MapGet("/tasks/{id:guid}", (Guid id, IHttpInvoker invoker, CancellationToken ct) =>
    invoker.InvokeAsync(new GetTaskQuery(id), ct));                         // 200 + body, or ProblemDetails

app.MapPost("/tasks", (CreateTaskCommand cmd, IHttpInvoker invoker, CancellationToken ct) =>
    invoker.InvokeAsync(cmd, id => Results.Created($"/tasks/{id}", id), ct)); // custom success

app.MapDelete("/tasks/{id:guid}", (Guid id, IHttpInvoker invoker, CancellationToken ct) =>
    invoker.InvokeAsync(new DeleteTaskCommand(id), ct));                    // 200 no body

app.MapGet("/tasks/stream", (IHttpInvoker invoker, CancellationToken ct) =>
    invoker.InvokeStreamAsync(new StreamTasksQuery(), ct));                 // failed items are skipped
```

## Controllers — `IMvcInvoker` (`UnambitiousFx.Synapse.AspNetCore.Mvc`)

```csharp
[ApiController]
[Route("tasks")]
public sealed class TasksController(IMvcInvoker invoker) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public ValueTask<IActionResult> Get(Guid id, CancellationToken ct) =>
        invoker.InvokeAsync(new GetTaskQuery(id), ct);

    [HttpPost]
    public ValueTask<IActionResult> Create(CreateTaskCommand cmd, CancellationToken ct) =>
        invoker.InvokeAsync(cmd, id => Created($"/tasks/{id}", id), ct);
}
```

## Failure → HTTP status

Failures go through `IFailureHttpMapper` (from `UnambitiousFx.Functional.AspNetCore.Mappers`); the default produces ProblemDetails from the failure type (`NotFoundFailure` → 404, `ValidationFailure`/`BadRequestFailure` → 400, `ConflictFailure` → 409, plain `Failure`/`ExceptionalFailure` → 500). So **return the right typed failure from the handler** (`Result.FailNotFound<T>(...)`, `Result.FailValidation<T>(...)`) instead of picking a status code in the endpoint.

Custom mapping: subclass `DefaultFailureHttpMapper` (its `GetFailureResponse(IFailure)` is virtual) or implement `IFailureHttpMapper`, and register it **before** `AddSynapseAspNetCore()`:

```csharp
builder.Services.AddSingleton<IFailureHttpMapper, MyFailureHttpMapper>();
builder.Services.AddSynapseAspNetCore();
```

## Rules

- Use `IHttpInvoker`/`IMvcInvoker` in endpoints; don't inject `IInvoker` and `Match` to `Results.*` by hand.
- Don't throw from handlers to get a 4xx; return a typed failure.
- Native AOT: `WebApplication.CreateSlimBuilder`, a `JsonSerializerContext` listing every request/response type, and the source-generated `RegisterGroup`.
