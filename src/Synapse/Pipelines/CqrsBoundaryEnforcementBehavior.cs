using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Abstractions.Exceptions;

namespace UnambitiousFx.Synapse.Pipelines;

/// <summary>
///     Pipeline behavior that enforces CQRS boundaries by preventing:
///     - Commands from being sent within command handlers
///     - Queries from being sent within query handlers
///     - Commands from being sent within query handlers
///     This variant handles requests that do not produce a response.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public sealed class CqrsBoundaryEnforcementBehavior<TRequest> : IRequestPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    private const string CQRSBoundaryEnforcementKey = "__CQRSBoundaryEnforcement";
    private const string CQRSBoundaryEnforcementNameKey = "__CQRSBoundaryEnforcement_Name";
    private readonly IContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CqrsBoundaryEnforcementBehavior{TRequest}" /> class.
    /// </summary>
    public CqrsBoundaryEnforcementBehavior(IContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async ValueTask<Result> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;
        ValidateBoundaries(_context, requestName);
        AddBoundaryMetadata(_context, requestName);

        var response = await next(request, cancellationToken);
        RemoveBoundaryMetadata(_context);
        return response;
    }

    private static void RemoveBoundaryMetadata(IContext context)
    {
        if (!context.RemoveMetadata(CQRSBoundaryEnforcementKey))
        {
            throw new CqrsBoundaryViolationException(
                "CQRS boundary enforcement metadata was missing when trying to remove it. This indicates a violation of the CQRS boundary enforcement behavior.");
        }

        context.RemoveMetadata(CQRSBoundaryEnforcementNameKey);
    }

    private static void AddBoundaryMetadata(IContext context, string requestName)
    {
        context.SetMetadata(CQRSBoundaryEnforcementKey, true);
        context.SetMetadata(CQRSBoundaryEnforcementNameKey, requestName);
    }

    private static void ValidateBoundaries(IContext context, string requestName)
    {
        if (!context.TryGetMetadata<bool>(CQRSBoundaryEnforcementKey, out var isInRequest) || !isInRequest)
        {
            return;
        }

        var previousRequestName = context.GetMetadata<string>(CQRSBoundaryEnforcementNameKey);
        throw new CqrsBoundaryViolationException(
            $"CQRS boundary violation: Cannot send request '{requestName}' within a request handler. Boundary was previously crossed by '{previousRequestName}'.");
    }
}

/// <summary>
///     Pipeline behavior that enforces CQRS boundaries by preventing:
///     - Commands from being sent within command handlers
///     - Queries from being sent within query handlers
///     - Commands from being sent within query handlers
///     This variant handles requests that produce a response.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class CqrsBoundaryEnforcementBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private const string CQRSBoundaryEnforcementKey = "__CQRSBoundaryEnforcement";
    private const string CQRSBoundaryEnforcementNameKey = "__CQRSBoundaryEnforcement_Name";
    private readonly IContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CqrsBoundaryEnforcementBehavior{TRequest,TResponse}" /> class.
    /// </summary>
    public CqrsBoundaryEnforcementBehavior(IContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;
        ValidateBoundaries(_context, requestName);
        AddBoundaryMetadata(_context, requestName);

        var response = await next(request, cancellationToken);
        RemoveBoundaryMetadata(_context);
        return response;
    }

    private static void RemoveBoundaryMetadata(IContext context)
    {
        if (!context.RemoveMetadata(CQRSBoundaryEnforcementKey))
        {
            throw new CqrsBoundaryViolationException(
                "CQRS boundary enforcement metadata was missing when trying to remove it. This indicates a violation of the CQRS boundary enforcement behavior.");
        }

        context.RemoveMetadata(CQRSBoundaryEnforcementNameKey);
    }

    private static void AddBoundaryMetadata(IContext context, string requestName)
    {
        context.SetMetadata(CQRSBoundaryEnforcementKey, true);
        context.SetMetadata(CQRSBoundaryEnforcementNameKey, requestName);
    }

    private static void ValidateBoundaries(IContext context, string requestName)
    {
        if (!context.TryGetMetadata<bool>(CQRSBoundaryEnforcementKey, out var isInRequest) || !isInRequest)
        {
            return;
        }

        var previousRequestName = context.GetMetadata<string>(CQRSBoundaryEnforcementNameKey);
        throw new CqrsBoundaryViolationException(
            $"CQRS boundary violation: Cannot send request '{requestName}' within a request handler. Boundary was previously crossed by '{previousRequestName}'.");
    }
}
