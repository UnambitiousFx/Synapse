using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.AspNetCore.Http;

namespace UnambitiousFx.Examples.MinimalApi.Counter;

/// <summary>
///     Registration and endpoint-mapping extensions for the Counter feature. The host (MinimalApi) calls
///     <see cref="AddCounterFeature" /> and <see cref="MapCounterEndpoints" />; the feature's handlers and
///     behaviors are wired through this assembly's generated <c>RegisterGroup</c>.
/// </summary>
public static class CounterEndpoints
{
    public static IServiceCollection AddCounterFeature(this IServiceCollection services)
    {
        services.AddSingleton<CounterStore>();
        return services;
    }

    public static IEndpointRouteBuilder MapCounterEndpoints(this IEndpointRouteBuilder app)
    {
        var counter = app.MapGroup("/counter");

        // Value-type (int) response flowing through the closed CQRS behavior + CounterTracingBehavior.
        counter.MapGet("/", async (
                [FromServices] IHttpInvoker invoker,
                CancellationToken ct) =>
            await invoker.InvokeAsync(new GetCounterQuery(), ct));

        counter.MapPost("/increment", async (
                [FromServices] IHttpInvoker invoker,
                CancellationToken ct) =>
            await invoker.InvokeAsync(new IncrementCounterCommand(), ct));

        // Demonstrates CQRS boundary enforcement: the handler sends a nested request and the pipeline rejects it.
        counter.MapPost("/illegal-nested", async (
                [FromServices] IHttpInvoker invoker,
                CancellationToken ct) =>
            await invoker.InvokeAsync(new IllegalNestedCommand(), ct));

        return app;
    }
}
