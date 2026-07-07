using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.MinimalApi.Features.Tasks;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Infrastructure.Pipelines;

// ═══════════════════════════════════════════════════════════════
// AuthorizationBehavior — short-circuiting + constraint demo
//
// Mechanism: source generator via [PipelineBehavior]. The generator cross-products this
// open-generic behavior with every discovered handler whose request type satisfies the
// generic constraint (where TRequest : ISecuredRequest), emitting closed registrations.
// This is Native-AOT safe even when TResponse is a value type — unlike runtime open-generic
// registration (cfg.AddOpenGeneric*), which throws at resolve time under AOT for value types.
//
// Short-circuit pattern: if the caller lacks the required permission, this behavior
// returns Result.Failure WITHOUT calling next(), so the handler never executes.
// The 🧹 PURGING log from PurgeCompletedTasksCommandHandler only appears in stdout
// when this behavior allows the call through.
// ═══════════════════════════════════════════════════════════════

/// <summary>
///     Authorization behavior for requests that do not produce a response.
///     Only applied when <typeparamref name="TRequest" /> implements <see cref="ISecuredRequest" />.
/// </summary>
[PipelineBehavior]
public sealed class AuthorizationBehavior<TRequest> : IRequestPipelineBehavior<TRequest>, IOrderedPipelineBehavior
    where TRequest : IRequest, ISecuredRequest
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthorizationBehavior<TRequest>> _logger;

    /// <summary>Runtime pipeline position — runs early (after CQRS), before metrics/audit.</summary>
    public uint Order => 5;

    public AuthorizationBehavior(
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthorizationBehavior<TRequest>> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async ValueTask<Result> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TRequest> next,
        CancellationToken cancellationToken = default)
    {
        var required = request.RequiredPermission;
        var granted = GetGrantedPermissions();

        if (!granted.Contains(required, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "🚫 [auth] {RequestName} denied — requires '{Permission}' (caller has: [{Granted}])",
                typeof(TRequest).Name, required, string.Join(", ", granted));

            // Short-circuit: return a typed authorization failure without calling next().
            // DefaultFailureHttpMapper maps UnauthorizedFailure → 401 Unauthorized (see known-issue 003).
            return Result.FailUnauthorized($"Requires permission '{required}'");
        }

        _logger.LogInformation(
            "✅ [auth] {RequestName} authorized for '{Permission}'",
            typeof(TRequest).Name, required);

        return await next(request, cancellationToken);
    }

    private string[] GetGrantedPermissions()
    {
        var header = _httpContextAccessor.HttpContext?
            .Request.Headers["X-User-Permissions"]
            .ToString();

        return string.IsNullOrWhiteSpace(header)
            ? []
            : header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

/// <summary>
///     Authorization behavior for requests that produce a response.
///     Only applied when <typeparamref name="TRequest" /> implements <see cref="ISecuredRequest" />.
/// </summary>
[PipelineBehavior]
public sealed class AuthorizationBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>,
    IOrderedPipelineBehavior
    where TRequest : IRequest<TResponse>, ISecuredRequest
    where TResponse : notnull
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthorizationBehavior<TRequest, TResponse>> _logger;

    /// <summary>Runtime pipeline position — runs early (after CQRS), before metrics/audit.</summary>
    public uint Order => 5;

    public AuthorizationBehavior(
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthorizationBehavior<TRequest, TResponse>> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async ValueTask<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var required = request.RequiredPermission;
        var granted = GetGrantedPermissions();

        if (!granted.Contains(required, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "🚫 [auth] {RequestName} denied — requires '{Permission}' (caller has: [{Granted}])",
                typeof(TRequest).Name, required, string.Join(", ", granted));

            // Short-circuit: return a typed authorization failure without calling next().
            // DefaultFailureHttpMapper maps UnauthorizedFailure → 401 Unauthorized (see known-issue 003).
            return Result.FailUnauthorized<TResponse>($"Requires permission '{required}'");
        }

        _logger.LogInformation(
            "✅ [auth] {RequestName} authorized for '{Permission}'",
            typeof(TRequest).Name, required);

        return await next(request, cancellationToken);
    }

    private string[] GetGrantedPermissions()
    {
        var header = _httpContextAccessor.HttpContext?
            .Request.Headers["X-User-Permissions"]
            .ToString();

        return string.IsNullOrWhiteSpace(header)
            ? []
            : header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
