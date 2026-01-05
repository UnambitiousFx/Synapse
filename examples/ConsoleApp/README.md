# Mediator Profiling Console App

This console application provides comprehensive profiling scenarios for the UnambitiousFx Mediator library. It's
designed to help you measure and optimize mediator performance across different use cases.

## Architecture

The project is organized into a clean, modular structure:

```
ConsoleApp/
├── Commands/              # Command definitions (requests with/without responses)
│   ├── SimpleCommand.cs
│   ├── CreateOrderCommand.cs
│   ├── ComplexCommand.cs
│   ├── ConcurrentCommand.cs
│   └── HighVolumeCommand.cs
├── Queries/               # Query definitions (read operations)
│   ├── GetOrderQuery.cs
│   └── OrderDto.cs
├── Events/                # Event definitions
│   ├── OrderProcessedEvent.cs
│   ├── OrderShippedEvent.cs
│   ├── PaymentCompletedEvent.cs
│   └── MetricsEvent.cs
├── Handlers/
│   ├── Requests/         # Request and command handlers
│   │   ├── SimpleCommandHandler.cs
│   │   ├── CreateOrderCommandHandler.cs
│   │   ├── GetOrderQueryHandler.cs
│   │   └── ...
│   └── Events/           # Event handlers
│       ├── OrderProcessedEventHandler.cs
│       ├── SendShippingNotificationHandler.cs
│       └── ...
├── Pipelines/            # Pipeline behaviors (cross-cutting concerns)
│   ├── LoggingPipelineBehavior.cs
│   ├── ValidationPipelineBehavior.cs
│   └── EventLoggingPipelineBehavior.cs
├── Program.cs            # Main profiling orchestration
└── README.md             # This file
```

designed to help you measure and optimize mediator performance across different use cases.

## Profiling Scenarios

### 1. Simple Command (No Response)

- **Purpose**: Tests basic command handling without response
- **Volume**: 100 commands
- **Use Case**: Fire-and-forget operations, state changes
- **Metrics**: Total time, average per command

### 2. Command with Response

- **Purpose**: Tests command handling with response objects
- **Volume**: 100 commands
- **Use Case**: Create operations that return created entity details
- **Metrics**: Total time, average per command

### 3. Query Requests

- **Purpose**: Tests query pattern (read-only operations)
- **Volume**: 100 queries
- **Use Case**: Data retrieval operations
- **Metrics**: Total time, average per query

### 4. Event Publishing

- **Purpose**: Tests event dispatch to event handlers
- **Volume**: 100 events
- **Use Case**: Domain events, notifications
- **Metrics**: Total time, average per event

### 5. High Volume

- **Purpose**: Tests throughput under high load
- **Volume**: 1,000 commands
- **Use Case**: Batch processing, stress testing
- **Metrics**: Total time, average per command, throughput (requests/second)

### 6. Pipeline Behaviors

- **Purpose**: Tests impact of request pipeline behaviors
- **Volume**: 100 commands
- **Use Case**: Cross-cutting concerns (logging, validation, caching)
- **Metrics**: Total time, average per command

### 7. Concurrent Requests

- **Purpose**: Tests parallel request processing
- **Volume**: 100 commands (10 parallel batches of 10)
- **Use Case**: Concurrent operations, multi-threaded scenarios
- **Metrics**: Total time, average per command

## How to Use for Profiling

### Basic Profiling

Run the application to get timing metrics:

```bash
dotnet run
```

### CPU Profiling with dotnet-trace

```bash
# Install dotnet-trace if not already installed
dotnet tool install --global dotnet-trace

# Start profiling
dotnet-trace collect --process-id $(pgrep -f ConsoleApp) --providers Microsoft-Windows-DotNETRuntime

# Or use this to profile from start
dotnet-trace collect -- dotnet run
```

### Memory Profiling with dotnet-counters

```bash
# Install dotnet-counters
dotnet tool install --global dotnet-counters

# Monitor memory allocation
dotnet-counters monitor --process-id $(pgrep -f ConsoleApp) System.Runtime
```

### Advanced Profiling with Visual Studio or JetBrains Rider

1. Open the solution in your IDE
2. Set ConsoleApp as the startup project
3. Use the built-in profiler tools:
    - **Visual Studio**: Debug → Performance Profiler
    - **Rider**: Run → Profile

### PerfView (Windows)

```bash
# Download PerfView from GitHub
# Run with:
PerfView.exe run ConsoleApp.exe
```

## Customizing Scenarios

You can modify the scenarios by editing `Program.cs`:

- **Change volume**: Modify the loop counts (e.g., change 100 to 1000)
- **Add delays**: Simulate I/O operations with `await Task.Delay()`
- **Add complexity**: Include database or external API calls in handlers
- **Test pipelines**: Add custom pipeline behaviors to measure their impact

## Performance Baselines

These are example baselines on a modern development machine. Your results will vary:

- Simple Commands: < 0.1ms per command
- Commands with Response: < 0.1ms per command
- Queries: < 0.1ms per query
- Events: < 0.1ms per event
- High Volume Throughput: > 100,000 requests/second
- Concurrent Processing: Linear scaling with available CPU cores

## Adding New Scenarios

1. Create your command/query/event in `Commands.cs`, `Queries.cs`, or `Events.cs`
2. Create corresponding handlers in `Handlers.cs`
3. Add a new scenario method in `Program.cs`
4. Call your scenario from the main execution flow

Example:

```csharp
async Task RunMyCustomScenario(ISender sender)
{
    Console.WriteLine("\n--- My Custom Scenario ---");
    var stopwatch = Stopwatch.StartNew();
    
    // Your profiling logic here
    
    stopwatch.Stop();
    Console.WriteLine($"Completed in {stopwatch.ElapsedMilliseconds}ms");
}
```

## File Structure

- **Program.cs**: Main profiling orchestration and scenarios
- **Commands.cs**: Command definitions
- **Queries.cs**: Query definitions
- **Events.cs**: Event definitions
- **Handlers.cs**: All request/event handlers

## Tips for Accurate Profiling

1. **Warm-up**: Run scenarios once before measuring to JIT compile
2. **Multiple runs**: Execute multiple times and take averages
3. **Minimize noise**: Close other applications during profiling
4. **Release mode**: Build in Release mode for production-like performance
5. **Representative data**: Use realistic data sizes and complexity
6. **Isolate concerns**: Profile one thing at a time

## Release Mode Profiling

For production-representative results:

```bash
dotnet run -c Release
```

