using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace UnambitiousFx.Examples.MinimalApi.Tests;

/// <summary>
///     Integration tests for the Counter feature, which lives in a SEPARATE assembly
///     (examples/MinimalApi.Counter) and is composed into the host via its own generated RegisterGroup.
///     These exercise, over real HTTP: cross-assembly RegisterGroup composition, value-type (int) responses
///     through closed CQRS + open-generic [PipelineBehavior] registrations, and CQRS boundary enforcement.
/// </summary>
public sealed class CounterApiTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CounterApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Increment_ReturnsIncrementedIntValue()
    {
        // Arrange — read current value, then increment. Assert on the delta so the test is robust against
        // the shared singleton CounterStore and the ordering of other tests.
        var client = _factory.CreateClient();
        var before = await (await client.GetAsync("/counter", TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<int>(TestContext.Current.CancellationToken);

        // Act — value-type int response through the referenced library's RegisterGroup + closed CQRS + behavior.
        var response = await client.PostAsync("/counter/increment", content: null, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await response.Content.ReadFromJsonAsync<int>(TestContext.Current.CancellationToken);
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task Increment_RunsMetricsBehaviorRegisteredInMainAssembly()
    {
        // Arrange — MetricsBehavior is an open-generic [PipelineBehavior] declared in the MinimalApi
        // (composition root) assembly, while IncrementCounterCommand's handler lives in the referenced
        // MinimalApi.Counter assembly. Capture logs to observe the behavior firing across the boundary.
        var logs = new CapturingLoggerProvider();
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureLogging(lb => lb.AddProvider(logs)));
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsync("/counter/increment", content: null, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the main-assembly behavior wrapped the cross-assembly request.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(logs.Messages,
            m => m.Contains("[metrics:10]") && m.Contains("IncrementCounterCommand"));
    }

    [Fact]
    public async Task GetCounter_ReturnsInt()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/counter", TestContext.Current.CancellationToken);

        // Assert — value-type int response resolves and serializes.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = await response.Content.ReadFromJsonAsync<int>(TestContext.Current.CancellationToken);
        Assert.True(value >= 0);
    }

    [Fact]
    public async Task IllegalNested_TriggersCqrsBoundaryViolation()
    {
        // Arrange — Development environment so the 500 response carries the exception detail in its body.
        var factory = _factory.WithWebHostBuilder(b => b.UseEnvironment("Development"));
        var client = factory.CreateClient();

        var before = await (await client.GetAsync("/counter", TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<int>(TestContext.Current.CancellationToken);

        // Act — the handler sends a nested request, which the closed CqrsBoundaryEnforcementBehavior rejects.
        var response = await client.PostAsync("/counter/illegal-nested", content: null, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the request fails and the CQRS violation surfaces; the handler short-circuited (no state change).
        Assert.False(response.IsSuccessStatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("CQRS boundary", body, StringComparison.OrdinalIgnoreCase);

        var after = await (await client.GetAsync("/counter", TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<int>(TestContext.Current.CancellationToken);
        Assert.Equal(before, after);
    }

    /// <summary>Captures formatted log messages so tests can assert a pipeline behavior executed.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly ConcurrentQueue<string> _messages;

            public CapturingLogger(ConcurrentQueue<string> messages) => _messages = messages;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => _messages.Enqueue(formatter(state, exception));
        }
    }
}
