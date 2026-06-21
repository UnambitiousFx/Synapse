using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.MinimalApi.Features.Tasks;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Infrastructure.Pipelines;

// ═══════════════════════════════════════════════════════════════
// AuditBehavior — constraint-based open-generic demo (Order = 20)
//
// Mechanism: source-generator [PipelineBehavior] attribute with a
// GENERIC CONSTRAINT.  The generator's Satisfies() check
// (RegisterGroupFactory.cs) cross-products this behavior only with
// handlers whose request type implements IAuditableRequest.
//
// Effect at runtime:
//   CreateTaskCommand    : IAuditableRequest  → AuditBehavior applied ✅
//   UpdateTaskCommand    : IAuditableRequest  → AuditBehavior applied ✅
//   GetTaskQuery         (no marker)          → AuditBehavior skipped  ❌
//   PurgeCompletedTasksCommand               → AuditBehavior skipped  ❌
//
// Order = 20 → runs inside MetricsBehavior (10), so stdout shows:
//   ▶ [metrics:10] → 📝 [audit:20] → handler → 📝 exit → ◀
// ═══════════════════════════════════════════════════════════════

/// <summary>
///     Audit behavior for requests that do not produce a response.
///     Only applied to request types that implement <see cref="IAuditableRequest" />;
///     the source generator enforces this via the generic constraint.
/// </summary>
[PipelineBehavior(Order = 20)]
public sealed class AuditBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
    where TRequest : IRequest, IAuditableRequest
{
    private readonly ILogger<AuditBehavior<TRequest>> _logger;

    public AuditBehavior(ILogger<AuditBehavior<TRequest>> logger)
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

        _logger.LogInformation("📝 [audit:20] {RequestName} — pipeline entry recorded", name);

        var result = await next(request, cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        if (result.TryGetFailure(out var errors))
        {
            _logger.LogWarning("📝 [audit:20] {RequestName} failed in {Elapsed}: {Errors}",
                name, elapsed, errors.ToString());
        }
        else
        {
            _logger.LogInformation("📝 [audit:20] {RequestName} succeeded in {Elapsed}", name, elapsed);
        }

        return result;
    }
}

/// <summary>
///     Audit behavior for requests that produce a response.
///     Only applied to request types that implement <see cref="IAuditableRequest" />.
/// </summary>
[PipelineBehavior(Order = 20)]
public sealed class AuditBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IAuditableRequest
    where TResponse : notnull
{
    private readonly ILogger<AuditBehavior<TRequest, TResponse>> _logger;

    public AuditBehavior(ILogger<AuditBehavior<TRequest, TResponse>> logger)
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

        _logger.LogInformation("📝 [audit:20] {RequestName} — pipeline entry recorded", name);

        var result = await next(request, cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        if (!result.TryGet(out _, out var error))
        {
            _logger.LogWarning("📝 [audit:20] {RequestName} failed in {Elapsed}: {Error}",
                name, elapsed, error.ToString());
        }
        else
        {
            _logger.LogInformation("📝 [audit:20] {RequestName} succeeded in {Elapsed}", name, elapsed);
        }

        return result;
    }
}
