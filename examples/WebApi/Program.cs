using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Examples.Application.Application.Inventory;
using UnambitiousFx.Examples.Application.Application.Notifications;
using UnambitiousFx.Examples.Application.Application.Orders;
using UnambitiousFx.Examples.Application.Application.Orders.Fulfillment;
using UnambitiousFx.Examples.Application.Application.Payments;
using UnambitiousFx.Examples.Application.Application.Todos;
using UnambitiousFx.Examples.Application.Domain.Entities;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Examples.Application.Domain.Repositories;
using UnambitiousFx.Examples.Application.Infrastructure.Repositories;
using UnambitiousFx.Examples.Application.Infrastructure.Services;
using UnambitiousFx.Examples.WebApi.Models;
using UnambitiousFx.Functional.AspNetCore.Http.ValueTasks;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

const string applicationName = "WebApi";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(cfg =>
{
    cfg.AddRegisterGroup(new ManualRegisterGroup());

    // Todo handlers
    cfg.RegisterRequestHandler<CreateTodoCommandHandler, CreateTodoCommand, Guid>()
        .RegisterRequestHandler<ListTodoQueryHandler, ListTodoQuery, IEnumerable<Todo>>()
        .RegisterRequestHandler<UpdateTodoCommandHandler, UpdateTodoCommand>()
        .RegisterEventHandler<TodoUpdatedHandler, TodoUpdated>()
        .RegisterEventHandler<TodoDeletedHandler, TodoDeleted>();

    // Order handlers
    cfg.RegisterRequestHandler<CreateOrderCommandHandler, CreateOrderCommand, Guid>()
        .RegisterRequestHandler<ShipOrderCommandHandler, ShipOrderCommand>()
        .RegisterRequestHandler<CancelOrderCommandHandler, CancelOrderCommand>()
        .RegisterEventHandler<OrderCreatedHandler, OrderCreated>()
        .RegisterEventHandler<OrderShippedHandler, OrderShipped>()
        .RegisterEventHandler<OrderShippedNotificationHandler, OrderShipped>();

    // Fulfillment handlers (for cross-app communication)
    cfg.RegisterEventHandler<OrderFulfillmentRequestedHandler, OrderFulfillmentRequested>()
        .RegisterEventHandler<OrderFulfillmentCompletedHandler, OrderFulfillmentCompleted>();

    // Payment handlers
    cfg.RegisterRequestHandler<ProcessPaymentCommandHandler, ProcessPaymentCommand>()
        .RegisterEventHandler<PaymentProcessedHandler, PaymentProcessed>()
        .RegisterEventHandler<PaymentAnalyticsHandler, PaymentProcessed>();

    // Inventory handlers
    cfg.RegisterRequestHandler<UpdateInventoryCommandHandler, UpdateInventoryCommand>()
        .RegisterEventHandler<InventoryUpdatedHandler, InventoryUpdated>();

    // Notification handlers
    cfg.RegisterEventHandler<NotificationRequestedHandler, NotificationRequested>();

    cfg.RegisterRequestPipelineBehavior<SimpleLoggingBehavior>();
    cfg.RegisterEventPipelineBehavior<SimpleLoggingBehavior>();

    // Enable distributed messaging with transports
    cfg.EnableDistributedEvent(distributedConfig =>
            // Todo replace with working transport
            null!,
        //distributedConfig.ConfigureMessagingAsTransport(msgCfg =>
        //{
        //    msgCfg.ApplicationName(applicationName);
        //    msgCfg.UseAwsNameFormatter();
        //    msgCfg.UseBackgroundService();
        //    msgCfg.UseAws(awsConfig => { awsConfig.UseCredentialBuilder<LocalstackCredentialBuilder>(); });
        //    msgCfg.UseJsonSerializer(o =>
        //    {
        //        // todo register json source generator context
        //    });
        //}),
        transport =>
        {
            // ===== EXTERNAL EVENTS (sent through transport) =====
            // These will be automatically registered as publications in the messaging system

            // Order Management publishes these:
            transport.ConfigureEvent<OrderShipped>(opts => opts.AsExternal());
            transport.ConfigureEvent<OrderCancelled>(opts => opts.AsExternal());

            // Other external events:
            transport.ConfigureEvent<PaymentProcessed>(opts => opts.AsExternal());
            transport.ConfigureEvent<InventoryUpdated>(opts => opts.AsExternal());

            // Fulfillment events (published by WebApiAot):
            transport.ConfigureEvent<OrderFulfillmentRequested>(opts => opts.AsExternal());
            transport.ConfigureEvent<OrderFulfillmentCompleted>(opts => opts.AsExternal());

            // ===== LOCAL EVENTS (in-process only) =====
            transport.ConfigureEvent<OrderCreated>(opts => opts.AsLocal());
            transport.ConfigureEvent<NotificationRequested>(opts => opts.AsLocal());
            transport.ConfigureEvent<TodoUpdated>(opts => opts.AsLocal());
            transport.ConfigureEvent<TodoDeleted>(opts => opts.AsLocal());

            // ===== SUBSCRIPTIONS (consume from transport) =====
            // These will be automatically registered as subscriptions in the messaging system

            // Subscribe to fulfillment events from WebApiAot:
            transport.Subscribe<OrderFulfillmentRequested>();
            transport.Subscribe<OrderFulfillmentCompleted>();

            // Subscribe to other events (demo purposes):
            transport.Subscribe<PaymentProcessed>();
            transport.Subscribe<InventoryUpdated>();
        });
});
builder.Services.AddScoped<ITodoRepository, TodoRepository>();
builder.Services.AddSingleton<IFulfillmentService, InMemoryFulfillmentService>();
var app = builder.Build();
app.MapGet("/", () => "Hello World!");

var todoEndpoints = app.MapGroup("/todos");

todoEndpoints.MapGet("/{id:guid}", async ([FromRoute] Guid id,
    [FromServices] ISender sender,
    CancellationToken cancellationToken) =>
{
    var query = new TodoQuery { Id = id };
    return await sender.SendAsync<TodoQuery, Todo>(query, cancellationToken)
        .ToHttpResultAsync();
});

todoEndpoints.MapGet("/", async ([FromServices] IRequestHandler<ListTodoQuery, IEnumerable<Todo>> handler,
    [FromServices] IContextFactory contextFactory,
    CancellationToken cancellationToken) =>
{
    var query = new ListTodoQuery();

    return await handler.HandleAsync(query, cancellationToken)
        .ToHttpResultAsync();
});

todoEndpoints.MapPost("/", async ([FromServices] ISender sender,
    [FromBody] CreateTodoModel input,
    CancellationToken cancellationToken) =>
{
    var command = new CreateTodoCommand { Name = input.Name };

    return await sender.SendAsync<CreateTodoCommand, Guid>(command, cancellationToken)
        .ToHttpResultAsync();
});

todoEndpoints.MapPut("/{id:guid}", async ([FromServices] IRequestHandler<UpdateTodoCommand> handler,
    [FromServices] IContextFactory contextFactory,
    [FromRoute] Guid id,
    [FromBody] UpdateTodoModel input,
    CancellationToken cancellationToken) =>
{
    var command = new UpdateTodoCommand
    {
        Id = id,
        Name = input.Name
    };

    return await handler.HandleAsync(command, cancellationToken)
        .ToHttpResultAsync();
});

todoEndpoints.MapDelete("/{id:guid}", async ([FromServices] ISender sender,
    [FromRoute] Guid id,
    CancellationToken cancellationToken) =>
{
    var command = new DeleteTodoCommand { Id = id };

    return await sender.SendAsync(command, cancellationToken)
        .ToHttpResultAsync();
});

// ===== Transport Examples =====

var orderEndpoints = app.MapGroup("/orders");

orderEndpoints.MapPost("/", async ([FromServices] ISender sender,
    [FromBody] CreateOrderModel input,
    CancellationToken cancellationToken) =>
{
    var command = new CreateOrderCommand
    {
        CustomerName = input.CustomerName,
        TotalAmount = input.TotalAmount
    };

    return await sender.SendAsync<CreateOrderCommand, Guid>(command, cancellationToken)
        .ToCreatedHttpResultAsync(orderId => $"/orders/{orderId}",
            orderId => new { orderId });
});

orderEndpoints.MapPost("/{id:guid}/ship", async ([FromServices] ISender sender,
    [FromRoute] Guid id,
    CancellationToken cancellationToken) =>
{
    var command = new ShipOrderCommand { OrderId = id };

    return await sender.SendAsync(command, cancellationToken)
        .ToHttpResultAsync(() => new { message = "Order shipped successfully (EXTERNAL event sent to WebApiAot)" });
});

orderEndpoints.MapDelete("/{id:guid}", async ([FromServices] ISender sender,
    [FromRoute] Guid id,
    [FromBody] CancelOrderModel input,
    CancellationToken cancellationToken) =>
{
    var command = new CancelOrderCommand
    {
        OrderId = id,
        Reason = input.Reason
    };

    return await sender.SendAsync(command, cancellationToken)
        .ToHttpResultAsync(() => new { message = "Order cancelled (EXTERNAL event sent to WebApiAot)" });
});

var paymentEndpoints = app.MapGroup("/payments");

paymentEndpoints.MapPost("/", async ([FromServices] ISender sender,
    [FromBody] ProcessPaymentModel input,
    CancellationToken cancellationToken) =>
{
    var command = new ProcessPaymentCommand
    {
        OrderId = input.OrderId,
        Amount = input.Amount,
        PaymentMethod = input.PaymentMethod
    };

    return await sender.SendAsync(command, cancellationToken)
        .ToHttpResultAsync(() => new { message = "Payment processed (event sent to transport)" });
});

var inventoryEndpoints = app.MapGroup("/inventory");

inventoryEndpoints.MapPost("/{productId:guid}/update", async ([FromServices] ISender sender,
    [FromRoute] Guid productId,
    [FromBody] UpdateInventoryModel input,
    CancellationToken cancellationToken) =>
{
    var command = new UpdateInventoryCommand
    {
        ProductId = productId,
        QuantityChange = input.QuantityChange
    };

    return await sender.SendAsync(command, cancellationToken)
        .ToHttpResultAsync(() => new { message = "Inventory updated (event sent to transport)" });
});

app.Run();

namespace UnambitiousFx.Examples.WebApi
{
    public class Program;
}