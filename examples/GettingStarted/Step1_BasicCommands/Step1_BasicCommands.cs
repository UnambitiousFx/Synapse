using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.GettingStarted.Step1_BasicCommands;

/// <summary>
/// Step 1: Basic Commands and Queries
///
/// This step demonstrates:
/// - Commands without responses (IRequest)
/// - Commands with responses (IRequest&lt;TResponse&gt;)
/// - Queries (read-only requests)
/// - Using IInvoker to dispatch requests
/// </summary>
public static class Step1_BasicCommands
{
    public static async Task RunAsync(IInvoker invoker)
    {
        Console.WriteLine("\n─── Step 1: Basic Commands and Queries ───\n");

        // Example 1: Command without response (fire-and-forget)
        Console.WriteLine("1. Creating a task (command without response)...");
        var createCommand = new CreateTaskCommand { Title = "Learn Synapse", Description = "Study the getting started tutorial" };
        var createResult = await invoker.InvokeAsync(createCommand);

        if (createResult.IsSuccess)
            Console.WriteLine("   ✓ Task created successfully\n");
        else
            Console.WriteLine($"   ✗ Failed: {createResult}\n");

        // Example 2: Command with response
        Console.WriteLine("2. Creating a task and getting its ID (command with response)...");
        var createWithIdCommand = new CreateTaskWithIdCommand { Title = "Build an app", Description = "Create a real application" };
        var idResult = await invoker.InvokeAsync<CreateTaskWithIdCommand, Guid>(createWithIdCommand);

        if (idResult.TryGet(out var taskId, out var error))
            Console.WriteLine($"   ✓ Task created with ID: {taskId}\n");
        else
            Console.WriteLine($"   ✗ Failed: {error}\n");

        // Example 3: Query (read operation)
        Console.WriteLine("3. Querying a task by ID...");
        var query = new GetTaskQuery { TaskId = taskId };
        var taskResult = await invoker.InvokeAsync<GetTaskQuery, TaskDto>(query);

        if (taskResult.TryGet(out var task, out var queryError))
            Console.WriteLine($"   ✓ Found task: '{task.Title}' (Status: {task.Status})\n");
        else
            Console.WriteLine($"   ✗ Failed: {queryError}\n");

        // Example 4: Query returning a list
        Console.WriteLine("4. Listing all tasks...");
        var listQuery = new ListTasksQuery();
        var tasksResult = await invoker.InvokeAsync<ListTasksQuery, List<TaskDto>>(listQuery);

        if (tasksResult.TryGet(out var tasks, out var listError))
            Console.WriteLine($"   ✓ Found {tasks.Count} tasks\n");
        else
            Console.WriteLine($"   ✗ Failed: {listError}\n");
    }
}

// ═══════════════════════════════════════════════════════════════
// Commands (write operations that modify state)
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Command without response - use for fire-and-forget operations
/// </summary>
public sealed record CreateTaskCommand : IRequest
{
    public required string Title { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Command with response - use when you need to return a value (e.g., ID of created entity)
/// </summary>
public sealed record CreateTaskWithIdCommand : IRequest<Guid>
{
    public required string Title { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Command for updating task status
/// </summary>
public sealed record CompleteTaskCommand : IRequest
{
    public required Guid TaskId { get; init; }
}

// ═══════════════════════════════════════════════════════════════
// Queries (read-only operations that return data)
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Query to get a single task by ID
/// </summary>
public sealed record GetTaskQuery : IRequest<TaskDto>
{
    public required Guid TaskId { get; init; }
}

/// <summary>
/// Query to list all tasks
/// </summary>
public sealed record ListTasksQuery : IRequest<List<TaskDto>>;

// ═══════════════════════════════════════════════════════════════
// DTOs (Data Transfer Objects)
// ═══════════════════════════════════════════════════════════════

public sealed record TaskDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required Domain.TaskStatus Status { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
