using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Commands;

// Simple Command - No Response
public sealed record SimpleCommand : IRequest
{
    public required string Message { get; init; }
}

// High Volume Command

// Complex Command with Multiple Steps

// Concurrent Command