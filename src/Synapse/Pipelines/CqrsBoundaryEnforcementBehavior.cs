using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Pipeline behavior that enforces CQRS boundaries by preventing:
///     - Commands from being sent within command handlers
///     - Queries from being sent within query handlers
///     - Commands from being sent within query handlers
/// </summary>
public sealed class CqrsBoundaryEnforcementBehavior : IRequestPipelineBehavior
{
    private const string CQRSBoundaryEnforcementKey = "__CQRSBoundaryEnforcement";
    private const string CQRSBoundaryEnforcementNameKey = "__CQRSBoundaryEnforcement_Name";
    private readonly IContext _context;

    /// <summary>
    /// </summary>
    /// <param name="context"></param>
    public CqrsBoundaryEnforcementBehavior(IContext context)
    {
        _context = context;
    }

    /// <summary>
    /// </summary>
    /// <param name="request"></param>
    /// <param name="next"></param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="TRequest"></typeparam>
    /// <returns></returns>
    public async ValueTask<Result> HandleAsync<TRequest>(TRequest request,
        RequestHandlerDelegate next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        var requestName = typeof(TRequest).Name;
        ValidateBoundaries(_context, requestName);

        AddBoundaryMetadata(_context, requestName);

        var response = await next();
        RemoveBoundaryMetadata(_context);
        return response;
    }

    /// <summary>
    /// </summary>
    /// <param name="request"></param>
    /// <param name="next"></param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="TRequest"></typeparam>
    /// <typeparam name="TResponse"></typeparam>
    /// <returns></returns>
    public async ValueTask<Result<TResponse>> HandleAsync<TRequest, TResponse>(TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        var requestName = typeof(TRequest).Name;
        ValidateBoundaries(_context, requestName);

        AddBoundaryMetadata(_context, requestName);
        var response = await next();
        RemoveBoundaryMetadata(_context);
        return response;
    }

    private static void RemoveBoundaryMetadata(IContext context)
    {
        if (!context.RemoveMetadata(CQRSBoundaryEnforcementKey))
            throw new CqrsBoundaryViolationException(
                "CQRS boundary enforcement metadata was missing when trying to remove it. This indicates a violation of the CQRS boundary enforcement behavior.");

        context.RemoveMetadata(CQRSBoundaryEnforcementNameKey);
    }

    private static void AddBoundaryMetadata(IContext context,
        string requestName)
    {
        context.SetMetadata(CQRSBoundaryEnforcementKey, true);
        context.SetMetadata(CQRSBoundaryEnforcementNameKey, requestName);
    }

    private static void ValidateBoundaries(IContext context,
        string requestName)
    {
        if (!context.TryGetMetadata<bool>(CQRSBoundaryEnforcementKey, out var isInRequest) ||
            !isInRequest)
            return;

        var previousRequestName = context.GetMetadata<string>(CQRSBoundaryEnforcementNameKey);

        throw new CqrsBoundaryViolationException(
            $"CQRS boundary violation: Cannot send request '{requestName}' within a request handler. Boundary was previously crossed by '{previousRequestName}'.");
    }
}