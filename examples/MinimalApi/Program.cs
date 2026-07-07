using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Examples.MinimalApi;
using UnambitiousFx.Examples.MinimalApi.Features.Tasks;
using UnambitiousFx.Examples.MinimalApi.Features.Tasks.Validators;
using UnambitiousFx.Examples.MinimalApi.Infrastructure;
using UnambitiousFx.Examples.MinimalApi.Infrastructure.Pipelines;
using UnambitiousFx.Examples.MinimalApi.Modules.Notifications;
using UnambitiousFx.Examples.MinimalApi.Modules.Orders;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Pipelines;
using UnambitiousFx.Synapse.Publish.Orchestrators;

// ── Global pipeline behaviors (composition root) ──────────────────────────
// [assembly: SynapseGlobalBehavior(typeof(X<>))] registers an open-generic behavior once, here at the
// composition root. The source generator closes it over every matching handler — including handlers in
// REFERENCED assemblies (Orders, Notifications) — and emits one closed, Native-AOT-safe registration per
// match. This is how the host applies a behavior to plugged-in modules without those modules opting in.
// (Add [assembly: DisableSynapseCrossAssemblyBehaviors] to scope globals to this assembly's handlers only.)

// CQRS boundary enforcement, outermost. Closed registrations are required because open generics cannot
// close over value-type responses (e.g. Guid, int) under Native AOT — see known-issue 001.
[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior<>))]
[assembly: SynapseGlobalBehavior(typeof(CqrsBoundaryEnforcementBehavior<,>))]

// Built-in logging behaviors. They ship in the Synapse package, so they cannot carry [PipelineBehavior]
// at their source — the global-behavior attribute is the way to enable them from the consumer. Both arities
// close over reference types (IRequest / IEvent), and the one registration covers the modules too.
[assembly: SynapseGlobalBehavior(typeof(SimpleLoggingBehavior<>))]
[assembly: SynapseGlobalBehavior(typeof(SimpleLoggingEventBehavior<>))]

// Use CreateSlimBuilder for Native AOT compatibility
var builder = WebApplication.CreateSlimBuilder(args);

// Configure JSON serialization for Native AOT (all response types must be registered)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Infrastructure
builder.Services.AddSingleton<TaskRepository>();

// Modular-monolith modules — each is an independent assembly sharing only the Contracts library.
builder.Services.AddOrdersModule();
builder.Services.AddNotificationsModule();

// Required by AuthorizationBehavior to read the X-User-Permissions header
builder.Services.AddHttpContextAccessor();

// Synapse ASP.NET Core integration (IHttpInvoker, IMvcInvoker, IFailureHttpMapper)
builder.Services.AddSynapseAspNetCore();

builder.Services.AddSynapse(cfg =>
{
    // Every behavior is declared by an attribute, so wiring is just composing register groups.
    //
    // Pipeline order (outermost → innermost), decided by each behavior's IOrderedPipelineBehavior.Order
    // (default Last), NOT by registration order:
    //   [CqrsBoundaryEnforcement] → [Authorization:5] → [Metrics:10] → [Audit:20]
    //     → [SimpleLogging] → [RequestValidation] → [Handler]

    // Host assembly (Tasks feature) — SOURCE-GENERATOR path. The generated RegisterGroup auto-registers
    // every [RequestHandler<>] / [EventHandler<>] / [StreamRequestHandler<>] / [Validator] and the
    // host's own [PipelineBehavior] classes (Metrics, Audit, Authorization, StreamLogging). It also
    // implements IEventDispatcherRegistration, so AOT-safe event dispatch delegates are wired here too.
    cfg.AddRegisterGroup(new global::UnambitiousFx.Examples.MinimalApi.RegisterGroup());

    // Orders module — SOURCE-GENERATOR path, in a SEPARATE assembly. PlaceOrderCommandHandler carries
    // [RequestHandler<PlaceOrderCommand, Guid>]; the module's own generated RegisterGroup plugs in here.
    cfg.AddRegisterGroup(new global::UnambitiousFx.Examples.MinimalApi.Modules.Orders.RegisterGroup());

    // Notifications module — MANUAL path (no source generator in that assembly). AddNotificationsHandlers
    // calls cfg.RegisterEventHandler<OrderPlacedNotificationHandler, OrderPlacedEvent>(), which registers
    // the DI service and the AOT-safe dispatch delegate in one call.
    cfg.AddNotificationsHandlers();

    // Run both TaskCompleted event handlers concurrently (observe interleaved logs).
    cfg.SetEventOrchestrator<ConcurrentEventOrchestrator>();
});

var app = builder.Build();

// ── Correlation ID response header ────────────────────────────────────
// Reads the Synapse IContext.CorrelationId after the handler runs and adds it
// to the response so callers can trace requests end-to-end.
app.UseCorrelationId();

// ── Root ──────────────────────────────────────────────────────────────
app.MapGet("/", () => Results.Ok(new ApiInfo()));

// ── Modular-monolith: Orders + Notifications endpoints ────────────────
// POST /orders     — place an order; PlaceOrderCommandHandler emits OrderPlacedEvent
// GET  /notifications — list notifications recorded by OrderPlacedNotificationHandler
app.MapOrdersEndpoints();
app.MapNotificationsEndpoints();

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
            "Stream pipeline behavior — StreamLoggingBehavior counts yielded items [source-gen]",
            "Value-type response under AOT — PurgeCompletedTasks → int (closed CQRS + auth registrations)",
            // Cross-cutting
            "Context & correlation — IContext.CorrelationId → X-Correlation-Id header",
            "Global behaviors — [assembly: SynapseGlobalBehavior] registers CQRS + logging once, cross-assembly",
            // Modular monolith
            "Modular monolith (Orders) — cross-assembly event via source-generated RegisterGroup",
            "Modular monolith (Notifications) — cross-assembly event via manual cfg.RegisterEventHandler<>()"
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
            "POST   /tasks/admin/purge       — Purge completed tasks (AuthorizationBehavior, short-circuit)",
            "POST   /orders                  — Place order (Orders module, source-gen) — emits OrderPlacedEvent",
            "GET    /notifications           — List notifications (Notifications module, manual) — shows received events"
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
    [JsonSerializable(typeof(Guid))]               // body: Results.Created(..., result.TaskId) — also PlaceOrderCommand response
    [JsonSerializable(typeof(int))]                // PurgeCompletedTasksCommand response (value-type, AOT regression case)
    [JsonSerializable(typeof(ProblemDetails))]
    [JsonSerializable(typeof(HttpValidationProblemDetails))]
    // Modular-monolith example types
    [JsonSerializable(typeof(PlaceOrderRequest))]
    [JsonSerializable(typeof(NotificationEntry[]))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }

    // Make Program accessible for integration tests
    public class Program;
}
