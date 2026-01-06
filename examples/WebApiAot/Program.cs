using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Examples.Application.Application.Orders;
using UnambitiousFx.Examples.Application.Application.Orders.Fulfillment;
using UnambitiousFx.Examples.Application.Application.Todos;
using UnambitiousFx.Examples.Application.Domain.Entities;
using UnambitiousFx.Examples.Application.Domain.Events;
using UnambitiousFx.Examples.Application.Domain.Repositories;
using UnambitiousFx.Examples.Application.Infrastructure.Repositories;
using UnambitiousFx.Examples.Application.Infrastructure.Services;
using UnambitiousFx.Examples.WebApiAot.Models;
using UnambitiousFx.Functional.AspNetCore.Http.ValueTasks;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Pipelines;

const string applicationName = "WebApiAot";

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddSynapse(cfg =>
{
    // Todo handlers (keep for demo)
    cfg.RegisterRequestHandler<CreateTodoCommandHandler, CreateTodoCommand, Guid>()
        .RegisterRequestHandler<DeleteTodoCommandHandler, DeleteTodoCommand>()
        .RegisterRequestHandler<ListTodoQueryHandler, ListTodoQuery, IEnumerable<Todo>>()
        .RegisterRequestHandler<TodoQueryHandler, TodoQuery, Todo>()
        .RegisterRequestHandler<UpdateTodoCommandHandler, UpdateTodoCommand>();

    // Fulfillment handlers (WebApiAot is the fulfillment system)
    cfg.RegisterRequestHandler<RequestFulfillmentCommandHandler, RequestFulfillmentCommand, Guid>()
        .RegisterRequestHandler<CompleteFulfillmentCommandHandler, CompleteFulfillmentCommand>();

    // Event handlers for external events from WebApi
    cfg.RegisterEventHandler<OrderShippedHandler, OrderShipped>()
        .RegisterEventHandler<OrderCancelledHandler, OrderCancelled>();

    cfg.RegisterRequestPipelineBehavior<SimpleLoggingBehavior>();

    // Enable distributed messaging with transports
    cfg.EnableDistributedEvent(messaging =>
    {
        // Todo replace with working transport
        return null!;

        // Configure messaging transport (all-in-one configuration)
        //return messaging.ConfigureMessagingAsTransport(msgCfg =>
        //{
        //    msgCfg.ApplicationName(applicationName);
        //    msgCfg.UseAwsNameFormatter();
        //    msgCfg.UseBackgroundService();
        //    msgCfg.UseAws(awsConfig => { awsConfig.UseCredentialBuilder<LocalstackCredentialBuilder>(); });
        //    msgCfg.UseJsonSerializer(o =>
        //    {
        //        // todo register json source generator context
        //    });
        //});
    }, transport =>
    {
        // ===== EXTERNAL EVENTS (sent through transport) =====
        // These will be automatically registered as publications in the messaging system

        // Fulfillment system publishes these:
        transport.ConfigureEvent<OrderFulfillmentRequested>(opts => opts.AsExternal());
        transport.ConfigureEvent<OrderFulfillmentCompleted>(opts => opts.AsExternal());

        // Events from WebApi (Order Management):
        transport.ConfigureEvent<OrderShipped>(opts => opts.AsExternal());
        transport.ConfigureEvent<OrderCancelled>(opts => opts.AsExternal());

        // ===== SUBSCRIPTIONS (consume from transport) =====
        // These will be automatically registered as subscriptions in the messaging system

        // Subscribe to order events from WebApi:
        transport.Subscribe<OrderShipped>();
        transport.Subscribe<OrderCancelled>();
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
        .ToCreatedHttpResultAsync(id => $"/todos/{id}", id => new { id });
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

// ===== Fulfillment System Endpoints =====

var fulfillmentEndpoints = app.MapGroup("/fulfillment");

fulfillmentEndpoints.MapPost("/request", async ([FromServices] ISender sender,
    [FromBody] RequestFulfillmentModel input,
    CancellationToken cancellationToken) =>
{
    var command = new RequestFulfillmentCommand
    {
        OrderId = input.OrderId,
        WarehouseLocation = input.WarehouseLocation
    };

    return await sender.SendAsync<RequestFulfillmentCommand, Guid>(command, cancellationToken)
        .ToCreatedHttpResultAsync(fulfillmentId => $"/fulfillment/{fulfillmentId}",
            fulfillmentId => new { fulfillmentId });
});

fulfillmentEndpoints.MapPost("/{id:guid}/complete", async ([FromServices] ISender sender,
    [FromRoute] Guid id,
    CancellationToken cancellationToken) =>
{
    var command = new CompleteFulfillmentCommand { FulfillmentId = id };

    return await sender.SendAsync(command, cancellationToken)
        .ToHttpResultAsync(() => new { message = "Fulfillment completed (EXTERNAL event sent to WebApi)" });
});

fulfillmentEndpoints.MapGet("/", ([FromServices] IFulfillmentService fulfillmentService) =>
{
    var fulfillments = fulfillmentService.GetAllFulfillments();
    return Results.Ok(fulfillments);
});

fulfillmentEndpoints.MapGet("/{id:guid}", ([FromServices] IFulfillmentService fulfillmentService,
    [FromRoute] Guid id) =>
{
    var fulfillment = fulfillmentService.GetFulfillment(id);
    return fulfillment != null
        ? Results.Ok(fulfillment)
        : Results.NotFound(new { message = $"Fulfillment {id} not found" });
});

app.Run();

namespace UnambitiousFx.Examples.WebApiAot
{
    public class Program;
}