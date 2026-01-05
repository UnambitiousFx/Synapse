namespace UnambitiousFx.Examples.ConsoleApp.Commands;

public sealed record ComplexResponse
{
    public required string Result { get; init; }
    public required int ProcessedCount { get; init; }
}