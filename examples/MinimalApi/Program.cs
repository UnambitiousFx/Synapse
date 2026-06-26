using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Examples.MinimalApi;
using UnambitiousFx.Examples.MinimalApi.Features.Tasks;
using UnambitiousFx.Examples.MinimalApi.Features.Tasks.Validators;
using UnambitiousFx.Examples.MinimalApi.Infrastructure;
using UnambitiousFx.Examples.MinimalApi.Infrastructure.Pipelines;
using UnambitiousFx.Examples.MinimalApi.Counter;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Pipelines;
using UnambitiousFx.Synapse.Publish.Orchestrators;

// Opt into CQRS boundary enforcement. The source generator emits closed (Native-AOT safe)
// CqrsBoundaryEnforcementBehavior registrations — one per request handler, inserted at the front
// of the pipeline — instead of an open-generic descriptor that would throw at resolve time when
// the response is a value type (see known-issue 001).
[assembly: EnableSynapseCqrsBoundaryEnforcement]

// Use CreateSlimBuilder for Native AOT compatibility
var builder = WebApplication.CreateSlimBuilder(args);

// Configure JSON serialization for Native AOT (all response types must be registered)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Infrastructure
builder.Services.AddSingleton<TaskRepository>();

// Counter feature (separate assembly) — registers its CounterStore singleton.
builder.Services.AddCounterFeature();

// Required by AuthorizationBehavior to read the X-User-Permissions header
builder.Services.AddHttpContextAccessor();

// Synapse ASP.NET Core integration (IHttpInvoker, IMvcInvoker, IFailureHttpMapper)
builder.Services.AddSynapseAspNetCore();

builder.Services.AddSynapse(cfg =>
{
    // ── Handlers + source-gen pipeline behaviors ───────────────────────
    //
    // AddRegisterGroup activates two source-generator outputs:
    //   1. RegisterGroup     — auto-registers every [RequestHandler<>] /
    //                          [EventHandler<>] / [StreamRequestHandler<>]
    //                          in this assembly (replaces the manual
    //                          cfg.RegisterRequestHandler<...>() calls).
    //   2. MetricsBehavior   — [PipelineBehavior] + IOrderedPipelineBehavior (Order 10),
    //                          unconstrained. Applied to EVERY discovered request.
    //   3. AuditBehavior     — [PipelineBehavior] + IOrderedPipelineBehavior (Order 20),
    //                          constrained: where TRequest : IAuditableRequest.
    //                          Applied only to the four mutating commands;
    //                          queries and PurgeCompletedTasksCommand are skipped.
    //
    // Each behavior declares its pipeline position at runtime via IOrderedPipelineBehavior, so
    // Metrics (10) always wraps Audit (20) regardless of registration source or order.
    cfg.AddRegisterGroup(new global::UnambitiousFx.Examples.MinimalApi.RegisterGroup());

    // Compose the Counter feature, whose handlers/behaviors live in a SEPARATE assembly
    // (examples/MinimalApi.Counter) and are wired through ITS OWN generated RegisterGroup. This proves
    // cross-assembly RegisterGroup composition, including closed CQRS + value-type (int) pipeline registrations.
    cfg.AddRegisterGroup(new global::UnambitiousFx.Examples.MinimalApi.Counter.RegisterGroup());

    // Wires event dispatchers generated alongside RegisterGroup so that
    // IEmitter.EmitAsync can route TaskCreatedEvent, TaskCompletedEvent, etc.
    cfg.UseEventDispatcherRegistration<global::UnambitiousFx.Examples.MinimalApi.EventDispatcherRegistration>();

    // ── Validators ─────────────────────────────────────────────────────
    // CreateTaskCommandValidator carries [Validator], so the generated RegisterGroup above already
    // registered it together with RequestValidationBehavior<CreateTaskCommand, CreateTaskResult>.
    // No cfg.AddValidator<...>() call is required (that runtime API still exists for non-generated setups).

    // ── Pipeline behaviors ──────────────────────────────────────────────
    //
    // Resulting pipeline order (outermost → innermost):
    //   [CqrsBoundaryEnforcement]  ← source-gen, closed; IOrderedPipelineBehavior.First (outermost)
    //     [AuthorizationBehavior:5]  ← source-gen, only ISecuredRequest (short-circuit) [PipelineBehavior]
    //       [MetricsBehavior:10]     ← source-gen, unconstrained [PipelineBehavior]
    //         [AuditBehavior:20]     ← source-gen, only IAuditableRequest [PipelineBehavior]
    //           [SimpleLoggingBehavior]  ← runtime open-generic (request-only, ref type → AOT-safe; Last)
    //             [RequestValidationBehavior]  ← source-gen from [Validator]; IOrderedPipelineBehavior.Last
    //               [Handler]
    // Position is decided entirely by each behavior's IOrderedPipelineBehavior.Order (default Last),
    // not by registration order.

    // Open-generic logging — library behaviors that cannot carry [PipelineBehavior] (the generator
    // only scans this assembly, not referenced ones). Registered at runtime. The request-only and
    // event-only arities close over reference types (IRequest / IEvent), so they remain AOT-safe.
    // The response-bearing SimpleLoggingBehavior<,> is intentionally NOT registered — MetricsBehavior<,>
    // covers response-bearing requests as a closed [PipelineBehavior] registration instead.
    cfg.AddOpenGenericRequestPipelineBehavior(typeof(SimpleLoggingBehavior<>));
    cfg.AddOpenGenericEventPipelineBehavior(typeof(SimpleLoggingEventBehavior<>));

    // Stream-specific behavior — wraps IAsyncEnumerable<Result<TItem>> from next() and emits a
    // "🔢 Streamed N items" summary. Registered as a CLOSED generic for the concrete stream type so it
    // stays Native-AOT safe (the open-generic AddOpenGenericStreamRequestPipelineBehavior is annotated
    // [RequiresDynamicCode] because its item type could be a value type).
    cfg.RegisterStreamRequestPipelineBehavior<StreamLoggingBehavior<StreamTasksQuery, TaskDto>, StreamTasksQuery, TaskDto>();

    // Typed validation for CreateTaskCommand is wired by the [Validator] attribute on
    // CreateTaskCommandValidator (emitted into RegisterGroup as a closed, deduplicated registration),
    // so no manual RequestValidationBehavior registration is needed here.

    // ── Event orchestration ────────────────────────────────────────────
    // Both TaskCompleted handlers run concurrently (observe interleaved logs)
    cfg.SetEventOrchestrator<ConcurrentEventOrchestrator>();

    // ── CQRS enforcement ────────────────────────────────────────────────
    // Opted-in via [assembly: EnableSynapseCqrsBoundaryEnforcement] at the top of this file.
    // The generator emits closed CqrsBoundaryEnforcementBehavior<Req[,Resp]> registrations at the
    // front of the pipeline, so it stays outermost and is Native-AOT safe even for value-type responses.
});

var app = builder.Build();

// ── Correlation ID response header ────────────────────────────────────
// Reads the Synapse IContext.CorrelationId after the handler runs and adds it
// to the response so callers can trace requests end-to-end.
app.UseCorrelationId();

// ── Root ──────────────────────────────────────────────────────────────
app.MapGet("/", () => Results.Ok(new ApiInfo()));

// ── Counter feature endpoints (defined in examples/MinimalApi.Counter) ──
app.MapCounterEndpoints();

// ── Task endpoints ────────────────────────────────────────────────────
var tasks = app.MapGroup("/tasks").WithTags("Tasks");

// Feature: Query returning a list
tasks.MapGet("/", async (
        [FromServices] IHttpInvoker invoker,
        CancellationToken ct) =>
    await invoker.InvokeAsync(new ListTasksQuery(), ct))
    .WithName("ListTasks")
    .WithSummary("List all tasks");

// Feature: Streaming request — yields tasks via IAsyncEnumerable
tasks.MapGet("/stream", (
        [FromServices] IHttpInvoker invoker,
        CancellationToken ct) =>
    invoker.InvokeStreamAsync(new StreamTasksQuery(), ct))
    .WithName("StreamTasks")
    .WithSummary("Stream tasks one by one (IStreamRequest<T>)");

// Feature: Query returning a single item
tasks.MapGet("/{id:guid}", async (
        [FromRoute] Guid id,
        [FromServices] IHttpInvoker invoker,
        CancellationToken ct) =>
    await invoker.InvokeAsync(new GetTaskQuery { TaskId = id }, ct))
    .WithName("GetTask")
    .WithSummary("Get a task by ID");

// Feature: Command with typed response + RequestValidationBehavior + AuditBehavior
// Valid input → 201 Created with Location header
// Invalid input (empty title/description) → failure response
tasks.MapPost("/", async (
    [FromBody] CreateTaskRequest request,
    [FromServices] IHttpInvoker invoker,
    CancellationToken ct) =>
{
    var command = new CreateTaskCommand { Title = request.Title, Description = request.Description };
    return await invoker.InvokeAsync(
        command,
        result => Results.Created($"/tasks/{result.TaskId}", result.TaskId),
        ct);
})
    .WithName("CreateTask")
    .WithSummary("Create a new task (validated + audited)");

// Feature: Command without response + AuditBehavior
tasks.MapPut("/{id:guid}", async (
    [FromRoute] Guid id,
    [FromBody] UpdateTaskRequest request,
    [FromServices] IHttpInvoker invoker,
    CancellationToken ct) =>
{
    var command = new UpdateTaskCommand { TaskId = id, Title = request.Title, Description = request.Description };
    return await invoker.InvokeAsync(command, ct);
})
    .WithName("UpdateTask")
    .WithSummary("Update a task");

// Feature: Command that emits an event handled by 2 concurrent handlers + AuditBehavior
// Check stdout: both TaskCompletedLoggingHandler and TaskCompletedNotificationHandler run in parallel
tasks.MapPost("/{id:guid}/complete", async (
        [FromRoute] Guid id,
        [FromServices] IHttpInvoker invoker,
        CancellationToken ct) =>
    await invoker.InvokeAsync(new CompleteTaskCommand { TaskId = id }, ct))
    .WithName("CompleteTask")
    .WithSummary("Mark a task as complete (triggers 2 concurrent event handlers)");

// Feature: Command that emits a domain event (TaskDeletedEvent → TaskDeletedLoggingHandler) + AuditBehavior
tasks.MapDelete("/{id:guid}", async (
        [FromRoute] Guid id,
        [FromServices] IHttpInvoker invoker,
        CancellationToken ct) =>
    await invoker.InvokeAsync(new DeleteTaskCommand { TaskId = id }, ct))
    .WithName("DeleteTask")
    .WithSummary("Delete a task");

// Feature: Admin command protected by AuthorizationBehavior (short-circuit demo)
// Without X-User-Permissions: tasks:admin header → 401 (typed UnauthorizedFailure), handler never runs
// With    X-User-Permissions: tasks:admin header → 200, returns count of purged tasks
tasks.MapPost("/admin/purge", async (
        [FromServices] IHttpInvoker invoker,
        CancellationToken ct) =>
    await invoker.InvokeAsync(new PurgeCompletedTasksCommand(), ct))
    .WithName("PurgeCompletedTasks")
    .WithSummary("Purge completed tasks — requires X-User-Permissions: tasks:admin (AuthorizationBehavior demo)");

app.Run();

namespace UnambitiousFx.Examples.MinimalApi
{
    // ── HTTP request/response models ──────────────────────────────────

    public record CreateTaskRequest(string Title, string Description);

    public record UpdateTaskRequest(string Title, string Description);

    // ── API info (replaces anonymous type for AOT compatibility) ──────

    public sealed record ApiInfo
    {
        public string Name { get; init; } = "Synapse MinimalApi Example (Native AOT)";
        public string Description { get; init; } = "Executable feature tour of UnambitiousFx.Synapse";
        public bool AotReady { get; init; } = true;

        public string[] Features { get; init; } =
        [
            "Commands — with typed response (CreateTask → CreateTaskResult { TaskId })",
            "Commands — without response (UpdateTask, CompleteTask, DeleteTask)",
            "Queries — single item and list (GetTask, ListTasks)",
            "Streaming — IStreamRequest<T> yielded via IAsyncEnumerable (StreamTasks)",
            "Validation — RequestValidationBehavior<TRequest, TResponse> (CreateTask)",
            "Domain events — fan-out to multiple handlers (TaskCompletedEvent × 2)",
            "Concurrent event orchestration — ConcurrentEventOrchestrator",
            // Pipeline behavior demos
            "Behavior ordering — MetricsBehavior(Order=10) wraps AuditBehavior(Order=20) [source-gen]",
            "Constraint-based open generic — AuditBehavior only on IAuditableRequest commands [source-gen]",
            "Short-circuit behavior — AuthorizationBehavior halts pipeline on missing permission [source-gen]",
            "Stream pipeline behavior — StreamLoggingBehavior counts yielded items [typed/closed]",
            "Value-type response under AOT — PurgeCompletedTasks → int (closed CQRS + auth registrations)",
            // Cross-cutting
            "Context & correlation — IContext.CorrelationId → X-Correlation-Id header",
            "CQRS boundary enforcement — [assembly: EnableSynapseCqrsBoundaryEnforcement]"
        ];

        public string[] Endpoints { get; init; } =
        [
            "GET    /                        — API info",
            "GET    /tasks                   — List tasks (Query, no audit)",
            "GET    /tasks/stream            — Stream tasks (IStreamRequest + StreamLoggingBehavior)",
            "GET    /tasks/{id}              — Get task (Query, no audit)",
            "POST   /tasks                   — Create task (Command → CreateTaskResult, validated + audited)",
            "PUT    /tasks/{id}              — Update task (Command, audited)",
            "POST   /tasks/{id}/complete     — Complete task (2 concurrent event handlers, audited)",
            "DELETE /tasks/{id}              — Delete task (domain event, audited)",
            "POST   /tasks/admin/purge       — Purge completed tasks (AuthorizationBehavior, short-circuit)"
        ];
    }

    // ── JSON source generation (required for Native AOT) ──────────────

    [JsonSerializable(typeof(ApiInfo))]
    [JsonSerializable(typeof(CreateTaskRequest))]
    [JsonSerializable(typeof(UpdateTaskRequest))]
    [JsonSerializable(typeof(TaskDto))]
    [JsonSerializable(typeof(List<TaskDto>))]
    [JsonSerializable(typeof(IAsyncEnumerable<TaskDto>))]
    [JsonSerializable(typeof(CreateTaskResult))]   // CreateTaskCommand response (TaskId unwrapped to Guid in endpoint)
    [JsonSerializable(typeof(Guid))]               // body: Results.Created(..., result.TaskId)
    [JsonSerializable(typeof(int))]                // PurgeCompletedTasksCommand response (value-type, AOT regression case)
    [JsonSerializable(typeof(ProblemDetails))]
    [JsonSerializable(typeof(HttpValidationProblemDetails))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }

    // Make Program accessible for integration tests
    public class Program;
}
