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
        CqrsBoundaryMarker.Validate(_context, requestName);
        CqrsBoundaryMarker.Add(_context, requestName);

        Result response;
        try
        {
            response = await next(request, cancellationToken);
        }
        catch
        {
            // Inner handler/behavior threw: clear the marker so a later send in the same
            // scope sees a clean boundary, but do not mask the original exception.
            CqrsBoundaryMarker.RemoveIfPresent(_context);
            throw;
        }

        CqrsBoundaryMarker.Remove(_context);
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
        CqrsBoundaryMarker.Validate(_context, requestName);
        CqrsBoundaryMarker.Add(_context, requestName);

        Result<TResponse> response;
        try
        {
            response = await next(request, cancellationToken);
        }
        catch
        {
            // Inner handler/behavior threw: clear the marker so a later send in the same
            // scope sees a clean boundary, but do not mask the original exception.
            CqrsBoundaryMarker.RemoveIfPresent(_context);
            throw;
        }

        CqrsBoundaryMarker.Remove(_context);
        return response;
    }
}

/// <summary>
///     Marks that a request boundary has been crossed in the current context, recording which request
///     crossed it.
/// </summary>
/// <remarks>
///     A context feature rather than baggage: this marker is meaningful only inside the process that set it
///     and must never travel to another service.
/// </remarks>
internal sealed class CqrsBoundaryFeature : IContextFeature
{
    public CqrsBoundaryFeature(string requestName)
    {
        RequestName = requestName;
    }

    /// <summary>
    ///     The name of the request that crossed the boundary.
    /// </summary>
    public string RequestName { get; }

    public string Name => "CqrsBoundary";
}

/// <summary>
///     Shared boundary-enforcement logic for the no-response and with-response CQRS behaviors,
///     keeping the marker handling, validation rule, and exception messages in a single place.
/// </summary>
internal static class CqrsBoundaryMarker
{
    public static void Remove(IContext context)
    {
        if (!context.TryGetFeature<CqrsBoundaryFeature>(out _))
        {
            throw new CqrsBoundaryViolationException(
                "CQRS boundary enforcement marker was missing when trying to remove it. This indicates a violation of the CQRS boundary enforcement behavior.");
        }

        context.RemoveFeature<CqrsBoundaryFeature>();
    }

    public static void RemoveIfPresent(IContext context)
    {
        context.RemoveFeature<CqrsBoundaryFeature>();
    }

    public static void Add(IContext context, string requestName)
    {
        context.SetFeature(new CqrsBoundaryFeature(requestName));
    }

    public static void Validate(IContext context, string requestName)
    {
        if (!context.TryGetFeature<CqrsBoundaryFeature>(out var marker))
        {
            return;
        }

        throw new CqrsBoundaryViolationException(
            $"CQRS boundary violation: Cannot send request '{requestName}' within a request handler. Boundary was previously crossed by '{marker!.RequestName}'.");
    }
}
