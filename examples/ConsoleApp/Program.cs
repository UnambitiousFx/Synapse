using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.ConsoleApp;
using UnambitiousFx.Examples.ConsoleApp.Commands;
using UnambitiousFx.Examples.ConsoleApp.Events;
using UnambitiousFx.Examples.ConsoleApp.Pipelines;
using UnambitiousFx.Examples.ConsoleApp.Queries;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;

// Setup DI container
var services = new ServiceCollection()
    .AddLogging(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    })
    .AddMetrics()
    .AddMediator(cfg => cfg.AddRegisterGroup(new RegisterGroup()))
    .AddScoped<IStreamRequestPipelineBehavior, StreamLoggingBehavior>()
    .BuildServiceProvider();

var sender = services.GetRequiredService<ISender>();
var publisher = services.GetRequiredService<IPublisher>();

Console.WriteLine("=== Mediator Profiling Console App ===\n");

// Run different profiling scenarios
await RunSimpleCommandScenario(sender);
await RunCommandWithResponseScenario(sender);
await RunQueryScenario(sender);
await RunStreamingQueryScenario(sender);
await RunEventPublishingScenario(publisher);
await RunMultipleEventHandlersScenario(publisher);
await RunHighVolumeEventScenario(publisher);
await RunHighVolumeScenario(sender);
await RunPipelineBehaviorScenario(sender);
await RunConcurrentRequestsScenario(sender);

Console.WriteLine("\n=== All profiling scenarios completed ===");

return;

// Scenario 1: Simple Command (no response)
async Task RunSimpleCommandScenario(ISender senderService)
{
    Console.WriteLine("\n--- Scenario 1: Simple Command (No Response) ---");
    var stopwatch = Stopwatch.StartNew();

    for (var i = 0; i < 100; i++)
    {
        var command = new SimpleCommand { Message = $"Command {i}" };
        var result = await senderService.SendAsync(command);

        if (!result.TryGet(out var error)) Console.WriteLine($"Failed with {error}");
    }

    stopwatch.Stop();
    Console.WriteLine($"Completed 100 simple commands in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Average: {stopwatch.ElapsedMilliseconds / 100.0}ms per command");
}

// Scenario 2: Command with Response
async Task RunCommandWithResponseScenario(ISender senderService)
{
    Console.WriteLine("\n--- Scenario 2: Command with Response ---");
    var stopwatch = Stopwatch.StartNew();

    for (var i = 0; i < 100; i++)
    {
        var command = new CreateOrderCommand
        {
            ProductName = $"Product {i}",
            Quantity = i + 1,
            Price = 99.99m * (i + 1)
        };
        var result = await senderService.SendAsync<CreateOrderCommand, OrderCreatedResponse>(command);

        if (!result.TryGet(out _, out var error)) Console.WriteLine($"Failed with {error}");
    }

    stopwatch.Stop();
    Console.WriteLine($"Completed 100 commands with response in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Average: {stopwatch.ElapsedMilliseconds / 100.0}ms per command");
}

// Scenario 3: Query
async Task RunQueryScenario(ISender senderService)
{
    Console.WriteLine("\n--- Scenario 3: Query Requests ---");
    var stopwatch = Stopwatch.StartNew();

    for (var i = 0; i < 100; i++)
    {
        var query = new GetOrderQuery { OrderId = Guid.NewGuid() };
        var result = await senderService.SendAsync<GetOrderQuery, OrderDto>(query);

        if (!result.TryGet(out _, out var error)) Console.WriteLine($"Failed with {error}");
    }

    stopwatch.Stop();
    Console.WriteLine($"Completed 100 queries in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Average: {stopwatch.ElapsedMilliseconds / 100.0}ms per query");
}

// Scenario 3.5: Streaming Query
async Task RunStreamingQueryScenario(ISender senderService)
{
    Console.WriteLine("\n--- Scenario 3.5: Streaming Query ---");
    var stopwatch = Stopwatch.StartNew();

    var streamQuery = new GetOrdersStreamQuery
    {
        PageSize = 50,
        TotalOrders = 500
    };
    var orderCount = 0;

    await foreach (var result in senderService.SendStreamAsync<GetOrdersStreamQuery, OrderDto>(streamQuery))
        if (result.TryGet(out _, out var error))
        {
            orderCount++;
        }
        else
        {
            var errorMsg = error.Message;
            Console.WriteLine($"Error receiving order: {errorMsg}");
        }

    stopwatch.Stop();
    Console.WriteLine($"Streamed {orderCount} orders in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Average: {stopwatch.ElapsedMilliseconds / (double)orderCount}ms per order");
    Console.WriteLine($"Throughput: {orderCount / (stopwatch.ElapsedMilliseconds / 1000.0):F2} orders/second");
}


// Scenario 4: Event Publishing
async Task RunEventPublishingScenario(IPublisher publisherService)
{
    Console.WriteLine("\n--- Scenario 4: Event Publishing ---");
    var stopwatch = Stopwatch.StartNew();

    for (var i = 0; i < 100; i++)
    {
        var @event = new OrderProcessedEvent
        {
            OrderId = Guid.NewGuid(),
            ProcessedAt = DateTime.UtcNow
        };
        var result = await publisherService.PublishAsync(@event);

        if (!result.TryGet(out var error)) Console.WriteLine($"Failed with {error}");
    }

    stopwatch.Stop();
    Console.WriteLine($"Completed 100 event publishes in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Average: {stopwatch.ElapsedMilliseconds / 100.0}ms per event");
}

// Scenario 5: Multiple Event Handlers
async Task RunMultipleEventHandlersScenario(IPublisher publisherService)
{
    Console.WriteLine("\n--- Scenario 5: Event Publishing to Multiple Handlers ---");
    var stopwatch = Stopwatch.StartNew();

    for (var i = 0; i < 100; i++)
    {
        var @event = new OrderShippedEvent
        {
            OrderId = Guid.NewGuid(),
            ShippedAt = DateTime.UtcNow,
            ShippingId = Guid.NewGuid()
        };
        var result = await publisherService.PublishAsync(@event);

        if (!result.TryGet(out var error)) Console.WriteLine($"Failed with {error}");
    }

    stopwatch.Stop();
    Console.WriteLine($"Completed 100 event publishes to multiple handlers in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Average: {stopwatch.ElapsedMilliseconds / 100.0}ms per event");
}

// Scenario 6: High Volume Events
async Task RunHighVolumeEventScenario(IPublisher publisherService)
{
    Console.WriteLine("\n--- Scenario 6: High Volume Event Publishing (1000 events) ---");
    var stopwatch = Stopwatch.StartNew();

    for (var i = 0; i < 1000; i++)
    {
        var @event = new OrderProcessedEvent
        {
            OrderId = Guid.NewGuid(),
            ProcessedAt = DateTime.UtcNow
        };
        await publisherService.PublishAsync(@event);
    }

    stopwatch.Stop();
    Console.WriteLine($"Completed 1000 high-volume event publishes in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Average: {stopwatch.ElapsedMilliseconds / 1000.0}ms per event");
    Console.WriteLine($"Throughput: {1000.0 / (stopwatch.ElapsedMilliseconds / 1000.0)} events/second");
}

// Scenario 7: High Volume
async Task RunHighVolumeScenario(ISender senderService)
{
    Console.WriteLine("\n--- Scenario 7: High Volume (1000 requests) ---");
    var stopwatch = Stopwatch.StartNew();

    for (var i = 0; i < 1000; i++)
    {
        var command = new HighVolumeCommand { Data = $"Data {i}" };
        await senderService.SendAsync(command);
    }

    stopwatch.Stop();
    Console.WriteLine($"Completed 1000 high-volume commands in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Average: {stopwatch.ElapsedMilliseconds / 1000.0}ms per command");
    Console.WriteLine($"Throughput: {1000.0 / (stopwatch.ElapsedMilliseconds / 1000.0)} requests/second");
}

// Scenario 8: Pipeline Behavior Impact
async Task RunPipelineBehaviorScenario(ISender senderService)
{
    Console.WriteLine("\n--- Scenario 8: Commands with Pipeline Behaviors ---");
    var stopwatch = Stopwatch.StartNew();

    for (var i = 0; i < 100; i++)
    {
        var command = new ComplexCommand
        {
            Step1 = $"Step1-{i}",
            Step2 = i,
            Step3 = DateTime.UtcNow
        };
        await senderService.SendAsync<ComplexCommand, ComplexResponse>(command);
    }

    stopwatch.Stop();
    Console.WriteLine($"Completed 100 complex commands in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Average: {stopwatch.ElapsedMilliseconds / 100.0}ms per command");
}

// Scenario 9: Concurrent Requests
async Task RunConcurrentRequestsScenario(ISender senderService)
{
    Console.WriteLine("\n--- Scenario 9: Concurrent Requests (10 parallel batches) ---");
    var stopwatch = Stopwatch.StartNew();

    var tasks = Enumerable.Range(0, 10)
        .Select(async batchIndex =>
        {
            for (var i = 0; i < 10; i++)
            {
                var command = new ConcurrentCommand
                {
                    BatchId = batchIndex,
                    ItemId = i
                };
                await senderService.SendAsync(command);
            }
        });

    await Task.WhenAll(tasks);

    stopwatch.Stop();
    Console.WriteLine($"Completed 100 concurrent commands in {stopwatch.ElapsedMilliseconds}ms");
    Console.WriteLine($"Average: {stopwatch.ElapsedMilliseconds / 100.0}ms per command");
}