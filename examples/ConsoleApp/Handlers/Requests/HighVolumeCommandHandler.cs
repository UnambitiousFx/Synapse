using UnambitiousFx.Examples.ConsoleApp.Commands;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Handlers.Requests;

[RequestHandler<HighVolumeCommand>]
public sealed class HighVolumeCommandHandler : IRequestHandler<HighVolumeCommand>
{
    public ValueTask<Result> HandleAsync(HighVolumeCommand request,
        CancellationToken cancellationToken = default)
    {
        // Minimal processing for high volume scenario
        return ValueTask.FromResult(Result.Success());
    }
}