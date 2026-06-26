using Microsoft.Extensions.Diagnostics.HealthChecks;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish.Outbox;

/// <summary>
///     Health check for monitoring outbox processing lag and failures.
/// </summary>
public sealed class OutboxHealthCheck : IHealthCheck
{
    private readonly OutboxHealthCheckOptions _options;
    private readonly IEventOutboxStorage _storage;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxHealthCheck" /> class.
    /// </summary>
    /// <param name="storage">The outbox storage to monitor.</param>
    /// <param name="options">Configuration options for health check thresholds.</param>
    public OutboxHealthCheck(IEventOutboxStorage storage, OutboxHealthCheckOptions? options = null)
    {
        _storage = storage;
        _options = options ?? new OutboxHealthCheckOptions();
    }

    /// <summary>
    ///     Performs the health check by examining outbox queue depth and failure counts.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A health check result indicating the outbox health status.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pendingCount = await _storage.GetPendingCountAsync(cancellationToken);
            var retryingCount = await _storage.GetRetryingCountAsync(cancellationToken);
            var deadLetterCount = await _storage.GetDeadLetterCountAsync(cancellationToken);
            var oldestPendingAge = await _storage.GetOldestPendingAgeAsync(cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["pending_count"] = pendingCount,
                ["retrying_count"] = retryingCount,
                ["dead_letter_count"] = deadLetterCount,
                ["oldest_pending_age_seconds"] = oldestPendingAge?.TotalSeconds ?? 0
            };

            // Check for critical conditions
            if (deadLetterCount >= _options.CriticalDeadLetterThreshold)
            {
                return HealthCheckResult.Unhealthy(
                    $"Outbox has {deadLetterCount} dead-lettered events (threshold: {_options.CriticalDeadLetterThreshold})",
                    data: data);
            }

            if (pendingCount >= _options.CriticalPendingThreshold)
            {
                return HealthCheckResult.Unhealthy(
                    $"Outbox has {pendingCount} pending events (threshold: {_options.CriticalPendingThreshold})",
                    data: data);
            }

            if (oldestPendingAge.HasValue && oldestPendingAge.Value > _options.CriticalLagThreshold)
            {
                return HealthCheckResult.Unhealthy(
                    $"Outbox has events pending for {oldestPendingAge.Value.TotalMinutes:F1} minutes (threshold: {_options.CriticalLagThreshold.TotalMinutes:F1} minutes)",
                    data: data);
            }

            // Check for degraded conditions
            if (deadLetterCount >= _options.DegradedDeadLetterThreshold)
            {
                return HealthCheckResult.Degraded(
                    $"Outbox has {deadLetterCount} dead-lettered events (threshold: {_options.DegradedDeadLetterThreshold})",
                    data: data);
            }

            if (pendingCount >= _options.DegradedPendingThreshold)
            {
                return HealthCheckResult.Degraded(
                    $"Outbox has {pendingCount} pending events (threshold: {_options.DegradedPendingThreshold})",
                    data: data);
            }

            if (oldestPendingAge.HasValue && oldestPendingAge.Value > _options.DegradedLagThreshold)
            {
                return HealthCheckResult.Degraded(
                    $"Outbox has events pending for {oldestPendingAge.Value.TotalMinutes:F1} minutes (threshold: {_options.DegradedLagThreshold.TotalMinutes:F1} minutes)",
                    data: data);
            }

            return HealthCheckResult.Healthy("Outbox is healthy", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Failed to check outbox health", ex);
        }
    }
}

/// <summary>
///     Configuration options for outbox health check thresholds.
/// </summary>
public sealed class OutboxHealthCheckOptions
{
    /// <summary>
    ///     Number of pending events that triggers a degraded status. Default is 1000.
    /// </summary>
    public int DegradedPendingThreshold { get; set; } = 1000;

    /// <summary>
    ///     Number of pending events that triggers an unhealthy status. Default is 5000.
    /// </summary>
    public int CriticalPendingThreshold { get; set; } = 5000;

    /// <summary>
    ///     Number of dead-lettered events that triggers a degraded status. Default is 10.
    /// </summary>
    public int DegradedDeadLetterThreshold { get; set; } = 10;

    /// <summary>
    ///     Number of dead-lettered events that triggers an unhealthy status. Default is 50.
    /// </summary>
    public int CriticalDeadLetterThreshold { get; set; } = 50;

    /// <summary>
    ///     Age of oldest pending event that triggers a degraded status. Default is 5 minutes.
    /// </summary>
    public TimeSpan DegradedLagThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Age of oldest pending event that triggers an unhealthy status. Default is 15 minutes.
    /// </summary>
    public TimeSpan CriticalLagThreshold { get; set; } = TimeSpan.FromMinutes(15);
}