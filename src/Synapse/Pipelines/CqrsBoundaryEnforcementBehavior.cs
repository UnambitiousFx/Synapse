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
public sealed class CqrsBoundaryEnforcementBehavior<TRequest> : IRequestPipelineBehavior<TRequest>,
    IOrderedPipelineBehavior
    where TRequest : IRequest
{
    private readonly IContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CqrsBoundaryEnforcementBehavior{TRequest}" /> class.
    /// </summary>
    public CqrsBoundaryEnforcementBehavior(IContext context)
    {
        _context = context;
    }

    /// <summary>
    ///     Runs outermost so the boundary marker wraps every other behavior in the chain.
    /// </summary>
    public uint Order => IOrderedPipelineBehavior.First;

    /// <inheritdoc />
    public async ValueTask<Result> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;
        CqrsBoundaryMetadata.Validate(_context, requestName);
        CqrsBoundaryMetadata.Add(_context, requestName);

        Result response;
        try
        {
            response = await next(request, cancellationToken);
        }
        catch
        {
            // Inner handler/behavior threw: clear the marker so a later send in the same
            // scope sees a clean boundary, but do not mask the original exception.
            CqrsBoundaryMetadata.RemoveIfPresent(_context);
            throw;
        }

        CqrsBoundaryMetadata.Remove(_context);
        return response;
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
public sealed class CqrsBoundaryEnforcementBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>,
    IOrderedPipelineBehavior
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private readonly IContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CqrsBoundaryEnforcementBehavior{TRequest,TResponse}" /> class.
    /// </summary>
    public CqrsBoundaryEnforcementBehavior(IContext context)
    {
        _context = context;
    }

    /// <summary>
    ///     Runs outermost so the boundary marker wraps every other behavior in the chain.
    /// </summary>
    public uint Order => IOrderedPipelineBehavior.First;

    /// <inheritdoc />
    public async ValueTask<Result<TResponse>> HandleAsync(TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TRequest).Name;
        CqrsBoundaryMetadata.Validate(_context, requestName);
        CqrsBoundaryMetadata.Add(_context, requestName);

        Result<TResponse> response;
        try
        {
            response = await next(request, cancellationToken);
        }
        catch
        {
            // Inner handler/behavior threw: clear the marker so a later send in the same
            // scope sees a clean boundary, but do not mask the original exception.
            CqrsBoundaryMetadata.RemoveIfPresent(_context);
            throw;
        }

        CqrsBoundaryMetadata.Remove(_context);
        return response;
    }
}

/// <summary>
///     Shared boundary-enforcement metadata logic for the no-response and with-response CQRS behaviors,
///     keeping the metadata keys, validation rule, and exception messages in a single place.
/// </summary>
internal static class CqrsBoundaryMetadata
{
    private const string CQRSBoundaryEnforcementKey = "__CQRSBoundaryEnforcement";
    private const string CQRSBoundaryEnforcementNameKey = "__CQRSBoundaryEnforcement_Name";

    public static void Remove(IContext context)
    {
        if (!context.RemoveMetadata(CQRSBoundaryEnforcementKey))
        {
            throw new CqrsBoundaryViolationException(
                "CQRS boundary enforcement metadata was missing when trying to remove it. This indicates a violation of the CQRS boundary enforcement behavior.");
        }

        context.RemoveMetadata(CQRSBoundaryEnforcementNameKey);
    }

    public static void RemoveIfPresent(IContext context)
    {
        context.RemoveMetadata(CQRSBoundaryEnforcementKey);
        context.RemoveMetadata(CQRSBoundaryEnforcementNameKey);
    }

    public static void Add(IContext context, string requestName)
    {
        context.SetMetadata(CQRSBoundaryEnforcementKey, true);
        context.SetMetadata(CQRSBoundaryEnforcementNameKey, requestName);
    }

    public static void Validate(IContext context, string requestName)
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
