# Synapse MinimalApi Example (Native AOT Ready)

This example demonstrates how to use Synapse in an ASP.NET Core application with full Native AOT support.
It is the canonical ASP.NET integration example for the library.

## Features Demonstrated

- ✅ **Commands** — with (`CreateTask → Guid`) and without (`UpdateTask`, `CompleteTask`, `DeleteTask`) a typed response
- ✅ **Queries** — single item (`GetTask`) and list (`ListTasks`)
- ✅ **Streaming** — `IStreamRequest<T>` yielded via `IAsyncEnumerable` (`StreamTasks`)
- ✅ **Validation** — `RequestValidationBehavior<TRequest, TResponse>` wired up for `CreateTask`
- ✅ **Domain events** — fan-out to multiple handlers (`TaskCompletedEvent` × 2)
- ✅ **Concurrent event orchestration** — `ConcurrentEventOrchestrator`
- ✅ **Pipeline behaviors** — open-generic `SimpleLoggingBehavior` wrapping every request and event
- ✅ **Correlation** — `IContext.CorrelationId` propagated to `X-Correlation-Id` response header
- ✅ **CQRS boundary enforcement** — `EnableCqrsBoundaryEnforcement()`
- ✅ **Native AOT Compatibility** — `CreateSlimBuilder()` and JSON source generation

## Project Structure

```
MinimalApi/
├── Program.cs                          # Entry point: DI setup and endpoint mapping
├── Features/Tasks/
│   ├── Commands.cs                     # CreateTaskCommand, UpdateTaskCommand, ...
│   ├── Queries.cs                      # GetTaskQuery, ListTasksQuery, TaskDto, ...
│   ├── Events.cs                       # TaskCreatedEvent, TaskCompletedEvent, ...
│   ├── Handlers/
│   │   ├── CommandHandlers.cs
│   │   ├── QueryHandlers.cs
│   │   └── EventHandlers.cs
│   └── Validators/
│       └── TaskValidators.cs           # CreateTaskCommandValidator
└── Infrastructure/
    └── TaskRepository.cs               # In-memory ConcurrentDictionary store
```

## API Endpoints

| Method   | Path                    | Description                                     |
|----------|-------------------------|-------------------------------------------------|
| `GET`    | `/`                     | API info                                        |
| `GET`    | `/tasks`                | List all tasks (Query)                          |
| `GET`    | `/tasks/stream`         | Stream tasks one by one (IStreamRequest)        |
| `GET`    | `/tasks/{id}`           | Get a task by ID (Query)                        |
| `POST`   | `/tasks`                | Create a task (Command → Guid, validated)       |
| `PUT`    | `/tasks/{id}`           | Update a task (Command)                         |
| `POST`   | `/tasks/{id}/complete`  | Complete a task (2 concurrent event handlers)   |
| `DELETE` | `/tasks/{id}`           | Delete a task (domain event)                    |

## Running the Example

### Development (JIT)

```bash
cd examples/MinimalApi
dotnet run
```

### Publish as Native AOT

```bash
dotnet publish -c Release
```

The native executable will be in `bin/Release/net10.0/{runtime}/publish/`.

**Size comparison:**

| Publish mode    | Approximate size | Startup time |
|-----------------|------------------|--------------|
| Regular publish | ~90 MB           | 500–800 ms   |
| Native AOT      | ~15–25 MB        | 150–250 ms   |

## AOT-Specific Configurations

### 1. Slim Builder

```csharp
var builder = WebApplication.CreateSlimBuilder(args);
```

`CreateSlimBuilder` includes only essential services, resulting in smaller binaries.

### 2. JSON Source Generation

```csharp
[JsonSerializable(typeof(CreateTaskRequest))]
[JsonSerializable(typeof(UpdateTaskRequest))]
[JsonSerializable(typeof(TaskDto))]
[JsonSerializable(typeof(List<TaskDto>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0,
        AppJsonSerializerContext.Default);
});
```

Every type used in HTTP request/response bodies must be registered so the AOT compiler
can emit the necessary serialization code.

### 3. Project Configuration

```xml
<PropertyGroup>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

- `PublishAot` — enables Native AOT compilation on `dotnet publish`
- `InvariantGlobalization` — reduces binary size by using invariant culture only

## AOT Compatibility Notes

### ✅ Synapse is AOT-Ready

- No runtime reflection in hot paths
- Source generators for handler registration
- Trimming annotations
- `ValueTask` for minimal allocations

### ⚠️ What to Watch For

1. **Avoid Runtime Reflection**
   ```csharp
   // Bad (not AOT-friendly)
   var handler = Activator.CreateInstance(handlerType);

   // Good (AOT-friendly)
   var handler = new MyHandler(dependencies);
   ```

2. **JSON Serialization** — Always add `[JsonSerializable]` for types used in HTTP bodies.

3. **Dependency Injection** — Register services explicitly; avoid assembly scanning.

### Check for AOT Warnings

```bash
dotnet publish -c Release /p:PublishAot=true
```

Look for:
- `IL2026` — Methods that require runtime reflection
- `IL3050` — Trimming warnings

## When to Use Native AOT

### ✅ Good Fit

- Microservices and containers
- Serverless/FaaS (Azure Functions, AWS Lambda)
- CLI tools
- Resource-constrained environments
- Cold-start sensitive applications

### ❌ Not Ideal

- Applications heavy on runtime reflection
- Dynamic plugin systems
- Large dependency trees with non-AOT-ready libraries

## Next Steps

- Run `MinimalApi.Tests` for an integration test suite against these endpoints
- See [GettingStarted](../GettingStarted/README.md) for a step-by-step tutorial on Synapse fundamentals

## Learn More

- [.NET Native AOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Trim Warnings](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/fixing-warnings)
- [JSON Source Generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
