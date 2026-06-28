using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Examples.MinimalApi.Modules.Orders.Messages;
using UnambitiousFx.Synapse.AspNetCore.Http;

namespace UnambitiousFx.Examples.MinimalApi.Modules.Orders;

/// <summary>HTTP request body for placing an order.</summary>
public sealed record PlaceOrderRequest(string Product, int Quantity);

/// <summary>
///     Registration and endpoint-mapping extensions for the Orders module.
///     The host calls <see cref="AddOrdersModule" /> and <see cref="MapOrdersEndpoints" />.
///     Handler wiring is done through the source-generated <c>RegisterGroup</c>:
///     <code>cfg.AddRegisterGroup(new UnambitiousFx.Examples.MinimalApi.Modules.Orders.RegisterGroup());</code>
/// </summary>
public static class OrdersModule
{
    /// <summary>Registers Orders module infrastructure (OrderStore singleton).</summary>
    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        services.AddSingleton<OrderStore>();
        return services;
    }

    /// <summary>Maps the <c>/orders</c> endpoint group.</summary>
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders");

        // POST /orders — PlaceOrderCommand → Guid
        // The handler emits OrderPlacedEvent; the Notifications module reacts without being referenced here.
        orders.MapPost("/", async (
                [FromBody] PlaceOrderRequest request,
                [FromServices] IHttpInvoker invoker,
                CancellationToken ct) =>
            {
                var command = new PlaceOrderCommand(request.Product, request.Quantity);
                return await invoker.InvokeAsync(
                    command,
                    result => Results.Created($"/orders/{result}", result),
                    ct);
            })
            .WithName("PlaceOrder")
            .WithSummary("Place a new order — handler uses source-generated RegisterGroup, emits OrderPlacedEvent");

        return app;
    }
}
