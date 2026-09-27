using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish.Outbox;

/// <summary>
///     Polls the outbox and dispatches pending events in the background, alongside the manual
///     <see cref="IOutboxCommit.CommitAsync" /> path.
/// </summary>
/// <remarks>
///     Each poll runs in its own DI scope, because the storage is typically Scoped (backed by a <c>DbContext</c>)
///     and this service is a Singleton. Safe to run in several instances against one shared storage when that
///     storage implements <see cref="IClaimableOutboxStorage" />; a warning is logged at startup when it does not.
/// </remarks>
internal sealed class OutboxDispatcherService : BackgroundService
{
    private readonly ILogger<OutboxDispatcherService> _logger;
    private readonly OutboxDispatcherOptions _options;
    private readonly OutboxOptions _outboxOptions;
    private readonly IServiceScopeFactory _scopeFactory;

    public OutboxDispatcherService(IServiceScopeFactory scopeFactory,
        IOptions<OutboxDispatcherOptions> options,
        IOptions<OutboxOptions> outboxOptions,
        ILogger<OutboxDispatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _outboxOptions = outboxOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.PollingInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(OutboxDispatcherOptions)}.{nameof(OutboxDispatcherOptions.PollingInterval)} must be " +
                $"positive, but was {_options.PollingInterval}.");
        }

        // Everything before the first await runs on the host's startup path; yield so a slow first poll does
        // not hold up the rest of the application starting.
        await Task.Yield();

        WarnIfStorageCannotClaim();
        _logger.LogInformation("Outbox dispatcher started, polling every {PollingInterval}",
            _options.PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            var pollAgainNow = false;
            try
            {
                pollAgainNow = await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A storage outage must not stop the dispatcher for good; the next poll retries.
                _logger.LogError(ex, "Outbox dispatcher poll failed; retrying in {PollingInterval}",
                    _options.PollingInterval);
            }

            if (pollAgainNow)
            {
                continue;
            }

            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Outbox dispatcher stopped");
    }

    /// <returns>
    ///     <see langword="true" /> when the batch was full and fully successful, so more entries are likely
    ///     waiting. A full batch with failures waits for the next interval instead, so entries retried without a
    ///     back-off delay do not spin the loop.
    /// </returns>
    private async Task<bool> PollOnceAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOutboxManager>();
        var batch = await manager.ProcessBatchAsync(stoppingToken);

        return batch.Result.IsSuccess &&
               _outboxOptions.BatchSize is { } batchSize &&
               batch.Count >= batchSize;
    }

    private void WarnIfStorageCannotClaim()
    {
        using var scope = _scopeFactory.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IEventOutboxStorage>();
        if (storage is IClaimableOutboxStorage)
        {
            return;
        }

        _logger.LogWarning(
            "The outbox storage {StorageType} does not implement IClaimableOutboxStorage, so pending entries are " +
            "not claimed before dispatch. Running more than one instance of this application, or calling " +
            "IOutboxCommit.CommitAsync while the dispatcher runs, can dispatch the same entry twice",
            storage.GetType().Name);
    }
}
