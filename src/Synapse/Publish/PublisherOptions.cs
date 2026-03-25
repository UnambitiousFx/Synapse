using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish;

internal sealed record PublisherOptions
{
    public EmitMode DefaultMode { get; set; } = EmitMode.Now;
}