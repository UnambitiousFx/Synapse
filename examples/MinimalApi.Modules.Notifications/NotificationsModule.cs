using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Examples.MinimalApi.Modules.Contracts;
using UnambitiousFx.Examples.MinimalApi.Modules.Notifications.Handlers;
using UnambitiousFx.Synapse;

namespace UnambitiousFx.Examples.MinimalApi.Modules.Notifications;

/// <summary>
///     Registration and endpoint-mapping extensions for the Notifications module.
/// </summary>
/// <remarks>
///     Handler wiring is done <strong>manually</strong> via <see cref="AddNotificationsHandlers" />
///     instead of a source-generated <c>RegisterGroup</c>. This is the explicit, zero-codegen path:
///     every handler and its event dispatch delegate are registered with a single typed call.
/// </remarks>
public static class NotificationsModule
{
    /// <summary>Registers Notifications module infrastructure (NotificationLog singleton).</summary>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddSingleton<NotificationLog>();
        return services;
    }

    /// <summary>
    ///     Manually registers all Notifications event handlers into Synapse.
    ///     Called inside <c>AddSynapse(cfg => { ... })</c> in the host instead of
    ///     <c>cfg.AddRegisterGroup(new RegisterGroup())</c>.
    /// </summary>
    /// <remarks>
    ///     <c>RegisterEventHandler</c> registers both the DI service and the AOT-safe dispatch delegate
    ///     in one call — no additional <c>EventDispatcherOptions</c> configuration is required.
    /// </remarks>
    public static ISynapseConfig AddNotificationsHandlers(this ISynapseConfig cfg)
    {
        // Manual registration — equivalent to what the source generator would emit in RegisterGroup.
        // Add one call per handler; order does not matter for event handlers.
        cfg.RegisterEventHandler<OrderPlacedNotificationHandler, OrderPlacedEvent>();
        return cfg;
    }

    /// <summary>Maps the <c>/notifications</c> endpoint group.</summary>
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        var notifications = app.MapGroup("/notifications");

        // GET /notifications — returns all notification entries recorded by OrderPlacedNotificationHandler.
        // Place an order first: POST /orders { "product": "...", "quantity": 1 }
        notifications.MapGet("/", ([Microsoft.AspNetCore.Mvc.FromServices] NotificationLog log) =>
                Results.Ok(log.All))
            .WithName("GetNotifications")
            .WithSummary("List all order notifications received by the Notifications module (manually-registered handler)");

        return app;
    }
}
