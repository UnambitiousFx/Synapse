using Microsoft.Extensions.Logging;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Modules.Orders.Behaviors;

/// <summary>
///     Open-generic, response-bearing pipeline behavior registered via <c>[PipelineBehavior]</c>.
///     The source generator cross-products it with all request handlers discovered in this assembly
///     and emits <strong>closed</strong> registrations — here, closed over <c>Guid</c> (a value type).
/// </summary>
/// <remarks>
///     This mirrors <c>CounterTracingBehavior</c> in <c>MinimalApi.Counter</c> and demonstrates that
///     <c>[PipelineBehavior]</c> works correctly inside a cross-assembly module: the generator emits
///     <c>RegisterRequestPipelineBehavior&lt;OrderTracingBehavior&lt;PlaceOrderCommand, Guid&gt;, PlaceOrderCommand, Guid&gt;()</c>
///     in <c>RegisterGroup.g.cs</c>, which is Native-AOT safe even though <c>Guid</c> is a value type.
/// </remarks>
[PipelineBehavior]
public sealed class OrderTracingBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>,
    IOrderedPipelineBehavior
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
    private readonly ILogger<OrderTracingBehavior<TRequest, TResponse>> _logger;

    public OrderTracingBehavior(ILogger<OrderTracingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    /// <summary>Sits between MetricsBehavior (10) and the handler.</summary>
    public uint Order => 15;

    public async ValueTask<Result<TResponse>> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("▶ [orders:trace:15] {Request} started", typeof(TRequest).Name);
        var result = await next(request, cancellationToken);
        _logger.LogInformation("◀ [orders:trace:15] {Request} finished", typeof(TRequest).Name);
        return result;
    }
}
