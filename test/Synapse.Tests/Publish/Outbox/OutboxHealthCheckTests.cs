using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Publish.Outbox;

namespace UnambitiousFx.Synapse.Tests.Publish.Outbox;

public sealed class OutboxHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenStorageIsHealthy_ReturnsHealthy()
    {
        // Arrange (Given)
        var storage = CreateStorage(pending: 10, failed: 0, oldestAge: TimeSpan.FromMinutes(1));
        var check = new OutboxHealthCheck(storage);

        // Act (When)
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert (Then)
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(10, result.Data["pending_count"]);
        Assert.Equal(0, result.Data["failed_count"]);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenFailedCountExceedsCritical_ReturnsUnhealthy()
    {
        // Arrange (Given)
        var storage = CreateStorage(pending: 0, failed: 51, oldestAge: TimeSpan.FromMinutes(1));
        var check = new OutboxHealthCheck(storage);

        // Act (When)
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert (Then)
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("failed events", result.Description ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPendingCountExceedsCritical_ReturnsUnhealthy()
    {
        // Arrange (Given)
        var storage = CreateStorage(pending: 6000, failed: 0, oldestAge: TimeSpan.FromMinutes(1));
        var check = new OutboxHealthCheck(storage);

        // Act (When)
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert (Then)
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("pending events", result.Description ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenLagExceedsCritical_ReturnsUnhealthy()
    {
        // Arrange (Given)
        var storage = CreateStorage(pending: 1, failed: 0, oldestAge: TimeSpan.FromMinutes(20));
        var check = new OutboxHealthCheck(storage);

        // Act (When)
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert (Then)
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("pending for", result.Description ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenFailedCountExceedsDegraded_ReturnsDegraded()
    {
        // Arrange (Given)
        var storage = CreateStorage(pending: 0, failed: 10, oldestAge: TimeSpan.FromMinutes(1));
        var check = new OutboxHealthCheck(storage);

        // Act (When)
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert (Then)
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPendingCountExceedsDegraded_ReturnsDegraded()
    {
        // Arrange (Given)
        var storage = CreateStorage(pending: 1000, failed: 0, oldestAge: TimeSpan.FromMinutes(1));
        var check = new OutboxHealthCheck(storage);

        // Act (When)
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert (Then)
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenLagExceedsDegraded_ReturnsDegraded()
    {
        // Arrange (Given)
        var storage = CreateStorage(pending: 1, failed: 0, oldestAge: TimeSpan.FromMinutes(8));
        var check = new OutboxHealthCheck(storage);

        // Act (When)
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert (Then)
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenStorageThrows_ReturnsUnhealthy()
    {
        // Arrange (Given)
        var storage = Substitute.For<IEventOutboxStorage>();
        storage.When(x => x.GetPendingCountAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("storage error"));

        var check = new OutboxHealthCheck(storage);

        // Act (When)
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert (Then)
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    private static IEventOutboxStorage CreateStorage(int pending, int failed, TimeSpan? oldestAge)
    {
        var storage = Substitute.For<IEventOutboxStorage>();
        storage.GetPendingCountAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(pending));
        storage.GetFailedCountAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(failed));
        storage.GetOldestPendingAgeAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TimeSpan?>(oldestAge));
        return storage;
    }
}
