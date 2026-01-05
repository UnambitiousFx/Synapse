using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish;

internal sealed record PublisherOptions
{
    public PublishMode DefaultMode { get; set; } = PublishMode.Now;
}