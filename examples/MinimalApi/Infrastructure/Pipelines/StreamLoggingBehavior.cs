using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Infrastructure.Pipelines;

// ═══════════════════════════════════════════════════════════════
// StreamLoggingBehavior — stream pipeline behavior demo
//
// Mechanism: SOURCE GENERATOR via [PipelineBehavior]. The generator cross-products this
// open-generic behavior with every discovered stream handler (here StreamTasksQuery → TaskDto)
// and emits the closed registration — Native-AOT safe, no runtime open-generic descriptor.
//
// Demonstrates IStreamRequestPipelineBehavior<TRequest, TItem> — the streaming variant of the
// pipeline.  Unlike request behaviors, the "next" delegate returns IAsyncEnumerable<Result<TItem>>
// rather than a single Result.  This behavior wraps the enumerable to count yielded items and log a
// summary at stream end.
//
// Observe in stdout: "🔢 Streamed N StreamTasksQuery item(s) in <elapsed>"
// ═══════════════════════════════════════════════════════════════

/// <summary>
///     Streaming pipeline behavior that counts yielded items and logs a summary
///     after the stream completes.  Applied to all stream requests.
/// </summary>
[PipelineBehavior]
public sealed class StreamLoggingBehavior<TRequest, TItem> : IStreamRequestPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
    where TItem : notnull
{
    private readonly ILogger<StreamLoggingBehavior<TRequest, TItem>> _logger;

    public StreamLoggingBehavior(ILogger<StreamLoggingBehavior<TRequest, TItem>> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<Result<TItem>> HandleAsync(
        TRequest request,
        StreamRequestHandlerDelegate<TItem> next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var name = typeof(TRequest).Name;
        var startedAt = Stopwatch.GetTimestamp();
        var count = 0;

        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            count++;
            yield return item;
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        _logger.LogInformation(
            "🔢 Streamed {Count} {RequestName} item(s) in {Elapsed}",
            count, name, elapsed);
    }
}
