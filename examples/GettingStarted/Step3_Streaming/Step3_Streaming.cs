using System.Runtime.CompilerServices;
using UnambitiousFx.Examples.GettingStarted.Step1_BasicCommands;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.GettingStarted.Step3_Streaming;

/// <summary>
/// Step 3: Streaming Requests
///
/// This step demonstrates:
/// - IStreamRequest for returning large datasets incrementally
/// - Memory-efficient data streaming
/// - Cancellation support
/// </summary>
public static class Step3_Streaming
{
    public static async Task RunAsync(IInvoker invoker)
    {
        Console.WriteLine("─── Step 3: Streaming Requests ───\n");

        Console.WriteLine("Streaming tasks (demonstrates handling large datasets)...");

        var query = new StreamTasksQuery { PageSize = 5 };
        var count = 0;

        await foreach (var result in invoker.InvokeStreamAsync<StreamTasksQuery, TaskDto>(query))
        {
            if (result.TryGet(out var task, out var error))
            {
                count++;
                if (count <= 3) // Only show first 3
                    Console.WriteLine($"  → Received task: {task.Title}");
            }
            else
            {
                Console.WriteLine($"  ✗ Error: {error}");
            }
        }

        Console.WriteLine($"✓ Streamed {count} tasks total\n");
    }
}

// ═══════════════════════════════════════════════════════════════
// Streaming Requests
// ═══════════════════════════════════════════════════════════════

public sealed record StreamTasksQuery : IStreamRequest<TaskDto>
{
    public int PageSize { get; init; } = 10;
}

[StreamRequestHandler<StreamTasksQuery, TaskDto>]
public sealed class StreamTasksQueryHandler : IStreamRequestHandler<StreamTasksQuery, TaskDto>
{
    public async IAsyncEnumerable<Result<TaskDto>> HandleAsync(
        StreamTasksQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Simulate streaming from database
        for (var i = 0; i < 10; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            await Task.Delay(10, cancellationToken); // Simulate async I/O

            yield return Result.Success(new TaskDto
            {
                Id = Guid.NewGuid(),
                Title = $"Streamed Task {i + 1}",
                Description = "Generated for streaming demo",
                Status = Domain.TaskStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
