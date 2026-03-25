# Synapse WebApi Example

This example demonstrates how to integrate Synapse with ASP.NET Core for building a RESTful API.

## Features Demonstrated

- ✅ **ASP.NET Core Integration** - Using `IInvoker` and `IEmitter` in minimal APIs
- ✅ **Result-to-HTTP Mapping** - Converting `Result<T>` to HTTP responses with `.ToHttpResultAsync()`
- ✅ **Feature-Based Organization** - Organizing code by features (Tasks)
- ✅ **Commands and Queries** - CQRS pattern with Synapse
- ✅ **Event Publishing** - Domain events with multiple handlers
- ✅ **Pipeline Behaviors** - Cross-cutting concerns (logging)
- ✅ **Dependency Injection** - Proper service registration

## Project Structure

```
WebApi/
├── Program.cs                              # Entry point and endpoint definitions
├── Features/
│   └── Tasks/                              # Task feature module
│       ├── Commands.cs                     # Command definitions
│       ├── Queries.cs                      # Query definitions and DTOs
│       ├── Events.cs                       # Domain events
│       └── Handlers/
│           ├── CommandHandlers.cs          # Command handler implementations
│           ├── QueryHandlers.cs            # Query handler implementations
│           └── EventHandlers.cs            # Event handler implementations
└── Infrastructure/
    └── TaskRepository.cs                   # In-memory repository (demo)
```

## Running the Example

```bash
cd examples/WebApi
dotnet run
```

The API will be available at `http://localhost:5000` (or the port shown in console).

## API Endpoints

### Root
- `GET /` - API information and available endpoints

### Tasks
- `GET /tasks` - List all tasks
- `GET /tasks/{id}` - Get a specific task
- `POST /tasks` - Create a new task
- `PUT /tasks/{id}` - Update a task
- `POST /tasks/{id}/complete` - Mark task as complete
- `DELETE /tasks/{id}` - Delete a task

## Example Usage

### Create a Task

```bash
curl -X POST http://localhost:5000/tasks \
  -H "Content-Type: application/json" \
  -d '{"title": "Learn Synapse", "description": "Complete all examples"}'
```

Response:
```json
{
  "taskId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

### Get All Tasks

```bash
curl http://localhost:5000/tasks
```

Response:
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "title": "Learn Synapse",
    "description": "Complete all examples",
    "status": "Pending",
    "createdAt": "2026-03-24T17:00:00Z",
    "completedAt": null
  }
]
```

### Complete a Task

```bash
curl -X POST http://localhost:5000/tasks/3fa85f64-5717-4562-b3fc-2c963f66afa6/complete
```

## Key Concepts

### 1. Using IInvoker in Endpoints

```csharp
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
```

### 2. Result-to-HTTP Conversion

The `.ToHttpResultAsync()` extension methods (from `UnambitiousFx.Functional.AspNetCore`) automatically convert `Result<T>` to appropriate HTTP responses:

- `Success<T>` → `200 OK` with value
- `Failure` → `400 Bad Request` with error details

### 3. Event Publishing

```csharp
// In command handler
await _emitter.EmitAsync(new TaskCreatedEvent
{
    TaskId = task.Id,
    Title = task.Title,
    CreatedAt = task.CreatedAt
}, cancellationToken);
```

Multiple handlers can subscribe to the same event:
- `TaskCreatedLoggingHandler` - Logs the event
- Other handlers can be added for analytics, notifications, etc.

### 4. Feature-Based Organization

Instead of organizing by technical layers (Controllers, Services, etc.), we organize by features:
- All task-related code lives in `Features/Tasks/`
- Easy to find and modify related functionality
- Better encapsulation and maintainability

## Comparison with Traditional Architecture

| Traditional MVC | Feature-Based with Synapse |
|----------------|---------------------------|
| Controllers/ | Features/Tasks/ |
| Services/ | (Handlers inline with features) |
| Models/ | (Commands, Queries, Events inline) |
| Repositories/ | Infrastructure/ |

## Next Steps

- See [MinimalApi](../MinimalApi/README.md) for Native AOT version
- See [GettingStarted](../GettingStarted/README.md) for foundational concepts
- Check [Main Documentation](../../README.md) for full library features

## Learn More

- [ASP.NET Core Minimal APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- [CQRS Pattern](https://martinfowler.com/bliki/CQRS.html)
- [Result Pattern](https://github.com/UnambitiousFx/Functional)
