using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Examples.WebApi.Features.Tasks;
using UnambitiousFx.Examples.WebApi.Features.Tasks.Handlers;
using UnambitiousFx.Examples.WebApi.Infrastructure;
using UnambitiousFx.Functional.AspNetCore.Http.ValueTasks;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

// Use CreateSlimBuilder for Native AOT compatibility
var builder = WebApplication.CreateSlimBuilder(args);

// Configure JSON serialization for Native AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Add services
builder.Services.AddSingleton<TaskRepository>();
builder.Services.AddSynapse(cfg =>
{
    // Register handlers manually
    cfg.RegisterRequestHandler<CreateTaskCommandHandler, CreateTaskCommand, Guid>();
    cfg.RegisterRequestHandler<UpdateTaskCommandHandler, UpdateTaskCommand>();
    cfg.RegisterRequestHandler<CompleteTaskCommandHandler, CompleteTaskCommand>();
    cfg.RegisterRequestHandler<DeleteTaskCommandHandler, DeleteTaskCommand>();
    cfg.RegisterRequestHandler<GetTaskQueryHandler, GetTaskQuery, TaskDto>();
    cfg.RegisterRequestHandler<ListTasksQueryHandler, ListTasksQuery, List<TaskDto>>();

    // Register event handlers
    cfg.RegisterEventHandler<TaskCreatedLoggingHandler, TaskCreatedEvent>();
    cfg.RegisterEventHandler<TaskCompletedLoggingHandler, TaskCompletedEvent>();
    cfg.RegisterEventHandler<TaskCompletedNotificationHandler, TaskCompletedEvent>();
    cfg.RegisterEventHandler<TaskDeletedLoggingHandler, TaskDeletedEvent>();

    // Add pipeline behaviors
    cfg.RegisterRequestPipelineBehavior<SimpleLoggingBehavior>();
    cfg.RegisterEventPipelineBehavior<SimpleLoggingBehavior>();
});

var app = builder.Build();

// Root endpoint
app.MapGet("/", () => Results.Ok(new
{
    name = "Synapse Minimal API (Native AOT)",
    description = "Demonstrates Synapse with Native AOT compilation",
    aotReady = true,
    endpoints = new[]
    {
        "GET /tasks - List all tasks",
        "GET /tasks/{id} - Get task by ID",
        "POST /tasks - Create a new task",
        "PUT /tasks/{id} - Update a task",
        "POST /tasks/{id}/complete - Complete a task",
        "DELETE /tasks/{id} - Delete a task"
    }
}));

// ═══════════════════════════════════════════════════════════════
// Task Management Endpoints
// ═══════════════════════════════════════════════════════════════

var tasks = app.MapGroup("/tasks");

// GET /tasks - List all tasks
tasks.MapGet("/", async (
    [FromServices] IInvoker invoker,
    CancellationToken cancellationToken) =>
{
    var query = new ListTasksQuery();
    return await invoker.InvokeAsync<ListTasksQuery, List<TaskDto>>(query, cancellationToken)
        .ToHttpResultAsync();
});

// GET /tasks/{id} - Get task by ID
tasks.MapGet("/{id:guid}", async (
    [FromRoute] Guid id,
    [FromServices] IInvoker invoker,
    CancellationToken cancellationToken) =>
{
    var query = new GetTaskQuery { TaskId = id };
    return await invoker.InvokeAsync<GetTaskQuery, TaskDto>(query, cancellationToken)
        .ToHttpResultAsync();
});

// POST /tasks - Create a new task
tasks.MapPost("/", async (
    [FromBody] CreateTaskRequest request,
    [FromServices] IInvoker invoker,
    CancellationToken cancellationToken) =>
{
    var command = new CreateTaskCommand
    {
        Title = request.Title,
        Description = request.Description
    };

    return await invoker.InvokeAsync<CreateTaskCommand, Guid>(command, cancellationToken)
        .ToCreatedHttpResultAsync(
            taskId => $"/tasks/{taskId}",
            taskId => new { taskId });
});

// PUT /tasks/{id} - Update a task
tasks.MapPut("/{id:guid}", async (
    [FromRoute] Guid id,
    [FromBody] UpdateTaskRequest request,
    [FromServices] IInvoker invoker,
    CancellationToken cancellationToken) =>
{
    var command = new UpdateTaskCommand
    {
        TaskId = id,
        Title = request.Title,
        Description = request.Description
    };

    return await invoker.InvokeAsync(command, cancellationToken)
        .ToHttpResultAsync();
});

// POST /tasks/{id}/complete - Complete a task
tasks.MapPost("/{id:guid}/complete", async (
    [FromRoute] Guid id,
    [FromServices] IInvoker invoker,
    CancellationToken cancellationToken) =>
{
    var command = new CompleteTaskCommand { TaskId = id };

    return await invoker.InvokeAsync(command, cancellationToken)
        .ToHttpResultAsync(() => new { message = "Task completed successfully" });
});

// DELETE /tasks/{id} - Delete a task
tasks.MapDelete("/{id:guid}", async (
    [FromRoute] Guid id,
    [FromServices] IInvoker invoker,
    CancellationToken cancellationToken) =>
{
    var command = new DeleteTaskCommand { TaskId = id };

    return await invoker.InvokeAsync(command, cancellationToken)
        .ToHttpResultAsync();
});

app.Run();

// ═══════════════════════════════════════════════════════════════
// Request/Response Models
// ═══════════════════════════════════════════════════════════════

public record CreateTaskRequest(string Title, string Description);

public record UpdateTaskRequest(string Title, string Description);

// ═══════════════════════════════════════════════════════════════
// JSON Source Generation (Required for Native AOT)
// ═══════════════════════════════════════════════════════════════

[JsonSerializable(typeof(CreateTaskRequest))]
[JsonSerializable(typeof(UpdateTaskRequest))]
[JsonSerializable(typeof(TaskDto))]
[JsonSerializable(typeof(List<TaskDto>))]
[JsonSerializable(typeof(Guid))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}

// Make Program partial for testing
namespace UnambitiousFx.Examples.MinimalApi
{
    public class Program;
}