# Synapse Getting Started Tutorial

This tutorial demonstrates core Synapse features through a simple task management domain.

## What You'll Learn

This example is organized as a step-by-step tutorial:

### Step 1: Basic Commands and Queries
- Commands without responses (`IRequest`)
- Commands with responses (`IRequest<TResponse>`)
- Queries for read operations
- Using `IInvoker` to dispatch requests
- Working with `Result<T>` types

### Step 2: Events and Event Handlers
- Publishing events with `IEmitter`
- Multiple handlers subscribing to the same event
- Independent event handler execution

### Step 3: Streaming Requests
- `IStreamRequest<T>` for large datasets
- Memory-efficient streaming
- Async enumerable patterns

### Step 4-7: Advanced Topics
- Pipeline behaviors for cross-cutting concerns
- Context features and metadata
- Error handling patterns
- Request validation

## Running the Example

```bash
cd examples/GettingStarted
dotnet run
```

## Project Structure

```
GettingStarted/
├── Program.cs                    # Entry point and DI setup
├── Domain/                       # Domain entities and repository
│   ├── Task.cs
│   └── TaskRepository.cs
├── Step1_BasicCommands/          # Commands, queries, and handlers
├── Step2_Events/                 # Events and event handlers
├── Step3_Streaming/              # Streaming request examples
└── Step4-7/                      # Advanced feature demonstrations
```

## Key Concepts Demonstrated

### 1. **Request/Response Pattern**
```csharp
// Command without response
await invoker.InvokeAsync(new CreateTaskCommand { ... });

// Command with response
var result = await invoker.InvokeAsync<CreateTaskCommand, Guid>(command);
```

### 2. **Event Publishing**
```csharp
await emitter.EmitAsync(new TaskCompletedEvent { ... });
```

### 3. **Streaming**
```csharp
await foreach (var result in invoker.InvokeStreamAsync<Query, Item>(query))
{
    // Process each item as it arrives
}
```

### 4. **Result Types**
All handlers return `Result` or `Result<T>` for explicit error handling:
```csharp
if (result.TryGet(out var value, out var error))
    Console.WriteLine($"Success: {value}");
else
    Console.WriteLine($"Failed: {error}");
```

## Next Steps

After completing this tutorial:

1. **WebApi Example** - See how to integrate Synapse with ASP.NET Core
2. **MinimalApi Example** - Learn about Native AOT compatibility
3. **OutboxPattern Example** - Implement transactional event publishing

## Learn More

- [Main README](../../README.md) - Full library documentation
- [Migration Guide](../../docs/migration-from-mediatr.md) - Coming from MediatR?
- [Project Guidelines](../../CLAUDE.md) - Code style and best practices
