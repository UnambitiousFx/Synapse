using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Commands;

public sealed record ConcurrentCommand : IRequest
{
    public required int BatchId { get; init; }
    public required int ItemId { get; init; }
}