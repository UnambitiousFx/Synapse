using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.GettingStarted.Step2_Events;

/// <summary>
///     Step 2: Events and Event Handlers
///     This step demonstrates:
///     - Publishing events with IEmitter
///     - Multiple handlers for the same event
///     - Event handlers executing independently
/// </summary>
public static class Step2_Events
{
    public static async Task RunAsync(IEmitter emitter)
    {
        Console.WriteLine("─── Step 2: Events and Event Handlers ───\n");

        Console.WriteLine("Publishing a TaskCompleted event...");
        var @event = new TaskCompletedEvent
        {
            TaskId = Guid.NewGuid(),
            TaskTitle = "Learn about events",
            CompletedAt = DateTime.UtcNow
        };

        var result = await emitter.EmitAsync(@event);

        if (result.IsSuccess)
            Console.WriteLine("✓ Event published and handled by all subscribers\n");
        else
            Console.WriteLine($"✗ Failed: {result}\n");
    }
}

// ═══════════════════════════════════════════════════════════════
// Events
// ═══════════════════════════════════════════════════════════════

public sealed record TaskCompletedEvent : IEvent
{
    public required Guid TaskId { get; init; }
    public required string TaskTitle { get; init; }
    public required DateTime CompletedAt { get; init; }
}