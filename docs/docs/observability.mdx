---
sidebar_position: 13
---

# Observability

Synapse exposes structured logging, distributed tracing, metrics, and health checks out of the box. All hooks integrate with the standard .NET observability stack (Microsoft.Extensions.Logging, System.Diagnostics.ActivitySource, System.Diagnostics.Metrics, and `IHealthCheck`).

## Structured logging with built-in behaviors

### `SimpleLoggingBehavior`

Logs the name of each request or event and the elapsed time after the handler completes. Register it for requests, events, or both:

```csharp
services.AddSynapse(cfg =>
{
    cfg.RegisterRequestPipelineBehavior<SimpleLoggingBehavior>();
    cfg.RegisterEventPipelineBehavior<SimpleLoggingBehavior>();
});
```

Sample output:

```
info: Synapse[0] Handling CreateTaskCommand
info: Synapse[0] CreateTaskCommand completed in 4ms
```

### `LoggingEnrichmentBehavior<TRequest, TResponse>`

Enriches the `ILogger` scope with `CorrelationId` and all context metadata so every log entry emitted during the request automatically includes them — no manual `BeginScope` calls needed:

```csharp
cfg.RegisterRequestPipelineBehavior<
    LoggingEnrichmentBehavior<CreateTaskCommand, Guid>,
    CreateTaskCommand,
    Guid>();
```

Every `ILogger` call inside the handler or any downstream service will include:

```json
{
  "CorrelationId": "019550a7-0000-7000-0000-000000000000",
  "OccuredAt": "2026-04-08T10:00:00Z"
}
```

## Distributed tracing

Synapse creates an `ActivitySource` named `Unambitious.Synapse`. Any pipeline behavior or handler can start a child span using it:

```csharp
using System.Diagnostics;
using UnambitiousFx.Synapse;

public class TracingBehavior : IRequestPipelineBehavior
{
    public async ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        using var activity = SynapseActivitySource.Source.StartActivity(typeof(TRequest).Name);
        var result = await next();
        activity?.SetStatus(result.IsSuccess ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        return result;
    }

    public ValueTask<Result> HandleAsync<TRequest>(TRequest request, RequestHandlerDelegate next, CancellationToken ct)
        where TRequest : IRequest
        => next();
}
```

To export traces to an OpenTelemetry collector, register the activity source when configuring OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Unambitious.Synapse")
        .AddOtlpExporter());
```

## Metrics

`ISynapseMetrics` is backed by `System.Diagnostics.Metrics` and works with any OpenTelemetry-compatible metrics exporter.

| Metric                 | Type            | Description                           |
| ---------------------- | --------------- | ------------------------------------- |
| Event dispatched       | Counter         | Per-event type, success/failure label |
| Dispatch latency       | Histogram (ms)  | Per-event type                        |
| Outbox event processed | Counter         | Per-event type, success/failure label |
| Outbox dead-lettered   | Counter         | Per-event type                        |
| Outbox queue depth     | Gauge           | Total pending events                  |
| Outbox processing lag  | Gauge (seconds) | Age of the oldest pending event       |
| Outbox failed count    | Gauge           | Events awaiting retry                 |

Export to Prometheus + OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Unambitious.Synapse")
        .AddPrometheusExporter());
```

Replace the default implementation to customise all metric behaviour:

```csharp
builder.Services.AddSingleton<ISynapseMetrics, MyMetrics>();
```

## Outbox health check

`OutboxHealthCheck` implements `IHealthCheck` and reports the health of the event outbox. It monitors pending event count, failed event count, and queue lag.

### Register

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<OutboxHealthCheck>("outbox", tags: ["ready"]);
```

### Configure thresholds

```csharp
builder.Services.Configure<OutboxHealthCheckOptions>(opts =>
{
    opts.DegradedPendingThreshold  = 50;
    opts.CriticalPendingThreshold  = 200;
    opts.DegradedFailedThreshold   = 5;
    opts.CriticalFailedThreshold   = 20;
    opts.CriticalLagThreshold      = TimeSpan.FromMinutes(5);
});
```

| Threshold                  | Effect when exceeded      |
| -------------------------- | ------------------------- |
| `DegradedPendingThreshold` | Health status → Degraded  |
| `CriticalPendingThreshold` | Health status → Unhealthy |
| `DegradedFailedThreshold`  | Health status → Degraded  |
| `CriticalFailedThreshold`  | Health status → Unhealthy |
| `CriticalLagThreshold`     | Health status → Unhealthy |

### Expose the health endpoint

```csharp
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
});
```

## See also

- [Pipeline Behaviors](./pipelines) — `SimpleLoggingBehavior` and `LoggingEnrichmentBehavior` registration.
- [Outbox Pattern](./outbox) — outbox configuration and storage replacement.
