using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Infrastructure.Pipelines;

// ═══════════════════════════════════════════════════════════════
// MetricsBehavior — ordering demo (Order = 10)
//
// Mechanism: source-generator [PipelineBehavior] attribute.
// The generated RegisterGroup cross-products this open-generic
// behavior with EVERY discovered request/event handler, because
// it carries no extra constraints beyond what the interface requires.
//
// Order = 10 → outermost user-defined behavior (after CQRS enforcement
// at position 0).  Audit (Order = 20) always runs inside this one,
// which is visible in the stdout timing brackets.
// ═══════════════════════════════════════════════════════════════

/// <summary>
///     Timing behavior for requests that do not produce a response.
///     Emits enter/exit log lines so the pipeline nesting order is
///     visible in stdout even when other behaviors are stacked inside.
/// </summary>
[PipelineBehavior(Order = 10)]
public sealed class MetricsBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    private readonly ILogger<MetricsBehavior<TRequest>> _logger;

    public MetricsBehavior(ILogger<MetricsBehavior<TRequest>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<Result> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TRequest> next,
        CancellationToken cancellationToken = default)
    {
        var name = typeof(TRequest).Name;
        var startedAt = Stopwatch.GetTimestamp();

        _logger.LogInformation("▶ [metrics:10] {RequestName} started", name);

        var result = await next(request, cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        _logger.LogInformation("◀ [metrics:10] {RequestName} finished in {Elapsed}", name, elapsed);

        return result;
    }
}

/// <summary>
///     Timing behavior for requests that produce a response.
/// </summary>
[PipelineBehavior(Order = 10)]
public sealed class MetricsBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private readonly ILogger<MetricsBehavior<TRequest, TResponse>> _logger;

    public MetricsBehavior(ILogger<MetricsBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var name = typeof(TRequest).Name;
        var startedAt = Stopwatch.GetTimestamp();

        _logger.LogInformation("▶ [metrics:10] {RequestName} started", name);

        var result = await next(request, cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        _logger.LogInformation("◀ [metrics:10] {RequestName} finished in {Elapsed}", name, elapsed);

        return result;
    }
}
