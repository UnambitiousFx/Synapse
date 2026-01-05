using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.ConsoleApp.Pipelines;

/// <summary>
///     Pipeline behavior that logs streaming request execution details.
///     Tracks the number of items streamed and the total duration.
/// </summary>
public sealed class StreamLoggingBehavior : IStreamRequestPipelineBehavior
{
    private readonly ILogger<StreamLoggingBehavior> _logger;

    public StreamLoggingBehavior(ILogger<StreamLoggingBehavior> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<Result<TItem>> HandleAsync<TRequest, TItem>(TRequest request,
        StreamRequestHandlerDelegate<TItem> next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TRequest : IStreamRequest<TItem>
        where TItem : notnull
    {
        var sw = Stopwatch.StartNew();
        var count = 0;
        var errorCount = 0;

        _logger.LogInformation("Starting stream request {RequestType}", typeof(TRequest).Name);

        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            count++;

            if (!item.TryGet(out _, out var error))
            {
                errorCount++;
                var errorMsg = error.Message;
                _logger.LogWarning("Stream item {Count} failed with error: {Error}",
                    count,
                    errorMsg);
            }

            yield return item;

            // Log progress every 100 items
            if (count % 100 == 0) _logger.LogDebug("Streamed {Count} items so far...", count);
        }

        sw.Stop();
        _logger.LogInformation(
            "Stream request {RequestType} completed. Items: {Count}, Errors: {ErrorCount}, Duration: {Duration}ms",
            typeof(TRequest).Name,
            count,
            errorCount,
            sw.ElapsedMilliseconds);
    }
}