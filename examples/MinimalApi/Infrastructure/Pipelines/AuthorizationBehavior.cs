using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.MinimalApi.Features.Tasks;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Infrastructure.Pipelines;

// ═══════════════════════════════════════════════════════════════
// AuthorizationBehavior — short-circuiting + constraint demo
//
// Mechanism: RUNTIME open-generic registration via
//   cfg.AddOpenGenericRequestWithResponsePipelineBehavior(typeof(AuthorizationBehavior<,>))
// MS DI honors the generic constraint (where TRequest : ISecuredRequest) when
// closing the open generic at resolve time — descriptors for non-matching request
// types are silently skipped.
//
// Short-circuit pattern: if the caller lacks the required permission, this behavior
// returns Result.Failure WITHOUT calling next(), so the handler never executes.
// The 🧹 PURGING log from PurgeCompletedTasksCommandHandler only appears in stdout
// when this behavior allows the call through.
//
// No [PipelineBehavior] attribute — this is registered at runtime, NOT via the
// source generator.  This is intentional: it demonstrates the two mechanisms
// side by side (compare AuditBehavior which uses the attribute).
// ═══════════════════════════════════════════════════════════════

/// <summary>
///     Authorization behavior for requests that do not produce a response.
///     Only applied when <typeparamref name="TRequest" /> implements <see cref="ISecuredRequest" />.
/// </summary>
public sealed class AuthorizationBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
    where TRequest : IRequest, ISecuredRequest
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthorizationBehavior<TRequest>> _logger;

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

            // Short-circuit: return failure without calling next()
            return Result.Failure($"Forbidden: requires permission '{required}'");
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
public sealed class AuthorizationBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, ISecuredRequest
    where TResponse : notnull
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthorizationBehavior<TRequest, TResponse>> _logger;

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

            // Short-circuit: return failure without calling next()
            return Result.Failure<TResponse>($"Forbidden: requires permission '{required}'");
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
