using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Examples.MinimalApi;
using UnambitiousFx.Examples.MinimalApi.Features.Tasks;
using UnambitiousFx.Examples.MinimalApi.Features.Tasks.Handlers;
using UnambitiousFx.Examples.MinimalApi.Features.Tasks.Validators;
using UnambitiousFx.Examples.MinimalApi.Infrastructure;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Pipelines;
using UnambitiousFx.Synapse.Publish.Orchestrators;

// Use CreateSlimBuilder for Native AOT compatibility
var builder = WebApplication.CreateSlimBuilder(args);

// Configure JSON serialization for Native AOT (all response types must be registered)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Infrastructure
builder.Services.AddSingleton<TaskRepository>();

// Synapse ASP.NET Core integration (IHttpInvoker, IMvcInvoker, IFailureHttpMapper)
builder.Services.AddSynapseAspNetCore();

builder.Services.AddSynapse(cfg =>
{
    // ── Request handlers ───────────────────────────────────────────────
    cfg.RegisterRequestHandler<CreateTaskCommandHandler, CreateTaskCommand, Guid>();
    cfg.RegisterRequestHandler<UpdateTaskCommandHandler, UpdateTaskCommand>();
    cfg.RegisterRequestHandler<CompleteTaskCommandHandler, CompleteTaskCommand>();
    cfg.RegisterRequestHandler<DeleteTaskCommandHandler, DeleteTaskCommand>();
    cfg.RegisterRequestHandler<GetTaskQueryHandler, GetTaskQuery, TaskDto>();
    cfg.RegisterRequestHandler<ListTasksQueryHandler, ListTasksQuery, List<TaskDto>>();

    // ── Streaming request handler ──────────────────────────────────────
    // Demonstrates IStreamRequest<T> / IStreamRequestHandler<TRequest, TItem>
    cfg.RegisterStreamRequestHandler<StreamTasksQueryHandler, StreamTasksQuery, TaskDto>();

    // ── Event handlers ─────────────────────────────────────────────────
    cfg.RegisterEventHandler<TaskCreatedLoggingHandler, TaskCreatedEvent>();
    // Two handlers for the same event — fan-out pattern
    cfg.RegisterEventHandler<TaskCompletedLoggingHandler, TaskCompletedEvent>();
    cfg.RegisterEventHandler<TaskCompletedNotificationHandler, TaskCompletedEvent>();
    cfg.RegisterEventHandler<TaskDeletedLoggingHandler, TaskDeletedEvent>();

    // ── Validators ─────────────────────────────────────────────────────
    // Registers IRequestValidator<CreateTaskCommand> in DI
    cfg.AddValidator<CreateTaskCommandValidator, CreateTaskCommand, Guid>();

    // ── Pipeline behaviors ─────────────────────────────────────────────
    // Open-generic logging wraps every request and event (observe timings in stdout)
    cfg.AddOpenGenericRequestPipelineBehavior(typeof(SimpleLoggingBehavior<>));
    cfg.AddOpenGenericRequestWithResponsePipelineBehavior(typeof(SimpleLoggingBehavior<,>));
    cfg.AddOpenGenericEventPipelineBehavior(typeof(SimpleLoggingEventBehavior<>));
    // Validation runs for CreateTaskCommand before the handler — returns failure on invalid input
    cfg.RegisterRequestPipelineBehavior<RequestValidationBehavior<CreateTaskCommand, Guid>, CreateTaskCommand, Guid>();

    // ── Event orchestration ────────────────────────────────────────────
    // Both TaskCompleted handlers run concurrently (observe interleaved logs)
    cfg.SetEventOrchestrator<ConcurrentEventOrchestrator>();

    // ── CQRS enforcement ───────────────────────────────────────────────
    cfg.EnableCqrsBoundaryEnforcement();
});

var app = builder.Build();

// ── Correlation ID response header ────────────────────────────────────
// Reads the Synapse IContext.CorrelationId after the handler runs and adds it
// to the response so callers can trace requests end-to-end.
app.UseCorrelationId();

// ── Root ──────────────────────────────────────────────────────────────
app.MapGet("/", () => Results.Ok(new ApiInfo()));

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

// Feature: Command with typed response + RequestValidationBehavior
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
        id => Results.Created($"/tasks/{id}", id),
        ct);
})
    .WithName("CreateTask")
    .WithSummary("Create a new task (validated)");

// Feature: Command without response
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

// Feature: Command that emits an event handled by 2 concurrent handlers
// Check stdout: both TaskCompletedLoggingHandler and TaskCompletedNotificationHandler run in parallel
tasks.MapPost("/{id:guid}/complete", async (
        [FromRoute] Guid id,
        [FromServices] IHttpInvoker invoker,
        CancellationToken ct) =>
    await invoker.InvokeAsync(new CompleteTaskCommand { TaskId = id }, ct))
    .WithName("CompleteTask")
    .WithSummary("Mark a task as complete (triggers 2 concurrent event handlers)");

// Feature: Command that emits a domain event (TaskDeletedEvent → TaskDeletedLoggingHandler)
tasks.MapDelete("/{id:guid}", async (
        [FromRoute] Guid id,
        [FromServices] IHttpInvoker invoker,
        CancellationToken ct) =>
    await invoker.InvokeAsync(new DeleteTaskCommand { TaskId = id }, ct))
    .WithName("DeleteTask")
    .WithSummary("Delete a task");

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
            "Commands — with typed response (CreateTask → Guid)",
            "Commands — without response (UpdateTask, CompleteTask, DeleteTask)",
            "Queries — single item and list (GetTask, ListTasks)",
            "Streaming — IStreamRequest<T> yielded via IAsyncEnumerable (StreamTasks)",
            "Validation — RequestValidationBehavior<TRequest, TResponse> (CreateTask)",
            "Domain events — fan-out to multiple handlers (TaskCompletedEvent × 2)",
            "Concurrent event orchestration — ConcurrentEventOrchestrator",
            "Request pipeline behavior — SimpleLoggingBehavior (timing on every request)",
            "Event pipeline behavior — SimpleLoggingBehavior (timing on every event)",
            "Context & correlation — IContext.CorrelationId → X-Correlation-Id header",
            "CQRS boundary enforcement — EnableCqrsBoundaryEnforcement()"
        ];

        public string[] Endpoints { get; init; } =
        [
            "GET    /                    — API info",
            "GET    /tasks               — List tasks (Query)",
            "GET    /tasks/stream        — Stream tasks (IStreamRequest)",
            "GET    /tasks/{id}          — Get task (Query)",
            "POST   /tasks               — Create task (Command → Guid, validated)",
            "PUT    /tasks/{id}          — Update task (Command)",
            "POST   /tasks/{id}/complete — Complete task (2 concurrent event handlers)",
            "DELETE /tasks/{id}          — Delete task (domain event)"
        ];
    }

    // ── JSON source generation (required for Native AOT) ──────────────

    [JsonSerializable(typeof(ApiInfo))]
    [JsonSerializable(typeof(CreateTaskRequest))]
    [JsonSerializable(typeof(UpdateTaskRequest))]
    [JsonSerializable(typeof(TaskDto))]
    [JsonSerializable(typeof(List<TaskDto>))]
    [JsonSerializable(typeof(IAsyncEnumerable<TaskDto>))]
    [JsonSerializable(typeof(Guid))]
    [JsonSerializable(typeof(ProblemDetails))]
    [JsonSerializable(typeof(HttpValidationProblemDetails))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }

    // Make Program accessible for integration tests
    public class Program;
}
