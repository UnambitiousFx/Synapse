using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Examples.GettingStarted;
using UnambitiousFx.Examples.GettingStarted.Domain;
using UnambitiousFx.Examples.GettingStarted.Step1_BasicCommands;
using UnambitiousFx.Examples.GettingStarted.Step2_Events;
using UnambitiousFx.Examples.GettingStarted.Step3_Streaming;
using UnambitiousFx.Examples.GettingStarted.Step4_Pipelines;
using UnambitiousFx.Examples.GettingStarted.Step5_Context;
using UnambitiousFx.Examples.GettingStarted.Step6_ErrorHandling;
using UnambitiousFx.Examples.GettingStarted.Step7_Validation;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;

Console.WriteLine("╔══════════════════════════════════════════════╗");
Console.WriteLine("║  Synapse Getting Started Tutorial           ║");
Console.WriteLine("║  Learn Synapse features step by step        ║");
Console.WriteLine("╚══════════════════════════════════════════════╝\n");

// Setup DI container
var services = new ServiceCollection()
    .AddLogging(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    })
    .AddSingleton<TaskRepository>() // Register our in-memory repository
    .AddSynapse(cfg =>
    {
        // Register handlers using source generation.
        // RegisterGroup also implements IEventDispatcherRegistration — one call wires everything.
        cfg.AddRegisterGroup(new RegisterGroup());
    })
    .BuildServiceProvider();

var invoker = services.GetRequiredService<IInvoker>();
var emitter = services.GetRequiredService<IEmitter>();

// Run tutorial steps
await Step1_BasicCommands.RunAsync(invoker);
await Step2_Events.RunAsync(emitter);
await Step3_Streaming.RunAsync(invoker);
await Step4_Pipelines.RunAsync(invoker);
await Step5_Context.RunAsync(services);
await Step6_ErrorHandling.RunAsync(invoker);
await Step7_Validation.RunAsync(services);

Console.WriteLine("\n╔══════════════════════════════════════════════╗");
Console.WriteLine("║  Tutorial Complete!                          ║");
Console.WriteLine("║  Check the source code to learn more        ║");
Console.WriteLine("╚══════════════════════════════════════════════╝");