using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Examples.WebApi.Features.Tasks;
using UnambitiousFx.Examples.WebApi.Features.Tasks.Handlers;
using UnambitiousFx.Examples.WebApi.Infrastructure;
using UnambitiousFx.Functional.AspNetCore.Http;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<TaskRepository>();
builder.Services.AddSynapse(cfg =>
{
    // Register handlers manually (source generation will auto-discover these attributes)
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
    name = "Synapse WebApi Example",
    description = "Demonstrates Synapse integration with ASP.NET Core",
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

var tasks = app.MapGroup("/tasks").WithTags("Tasks");

// GET /tasks - List all tasks
tasks.MapGet("/", async (
        [FromServices] IInvoker invoker,
        CancellationToken cancellationToken) =>
    {
        var query = new ListTasksQuery();
        return await invoker.InvokeAsync<ListTasksQuery, List<TaskDto>>(query, cancellationToken)
            .ToHttpResult();
    })
    .WithName("ListTasks")
    .WithSummary("List all tasks");

// GET /tasks/{id} - Get task by ID
tasks.MapGet("/{id:guid}", async (
        [FromRoute] Guid id,
        [FromServices] IInvoker invoker,
        CancellationToken cancellationToken) =>
    {
        var query = new GetTaskQuery { TaskId = id };
        return await invoker.InvokeAsync<GetTaskQuery, TaskDto>(query, cancellationToken)
            .ToHttpResult();
    })
    .WithName("GetTask")
    .WithSummary("Get a task by ID");

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
            .ToCreatedHttpResult(
                taskId => $"/tasks/{taskId}",
                taskId => new { taskId });
    })
    .WithName("CreateTask")
    .WithSummary("Create a new task");

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
            .ToHttpResult();
    })
    .WithName("UpdateTask")
    .WithSummary("Update a task");

// POST /tasks/{id}/complete - Complete a task
tasks.MapPost("/{id:guid}/complete", async (
        [FromRoute] Guid id,
        [FromServices] IInvoker invoker,
        CancellationToken cancellationToken) =>
    {
        var command = new CompleteTaskCommand { TaskId = id };

        return await invoker.InvokeAsync(command, cancellationToken)
            .ToHttpResult(() => new { message = "Task completed successfully" });
    })
    .WithName("CompleteTask")
    .WithSummary("Mark a task as complete");

// DELETE /tasks/{id} - Delete a task
tasks.MapDelete("/{id:guid}", async (
        [FromRoute] Guid id,
        [FromServices] IInvoker invoker,
        CancellationToken cancellationToken) =>
    {
        var command = new DeleteTaskCommand { TaskId = id };

        return await invoker.InvokeAsync(command, cancellationToken)
            .ToHttpResult();
    })
    .WithName("DeleteTask")
    .WithSummary("Delete a task");

app.Run();

// ═══════════════════════════════════════════════════════════════
// Request/Response Models
// ═══════════════════════════════════════════════════════════════

public record CreateTaskRequest(string Title, string Description);

public record UpdateTaskRequest(string Title, string Description);

// Make Program partial for testing
namespace UnambitiousFx.Examples.WebApi
{
    public class Program;
}