using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Events;

// Event published when an order is processed
public sealed record OrderProcessedEvent : IEvent
{
    public required Guid OrderId { get; init; }
    public required DateTime ProcessedAt { get; init; }
}

// Event with multiple handlers

// High frequency event for volume testing

// Event with multiple handlers for testing parallel handler execution