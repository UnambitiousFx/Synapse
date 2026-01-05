using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Commands;

public sealed record ComplexCommand : IRequest<ComplexResponse>
{
    public required string Step1 { get; init; }
    public required int Step2 { get; init; }
    public required DateTime Step3 { get; init; }
}