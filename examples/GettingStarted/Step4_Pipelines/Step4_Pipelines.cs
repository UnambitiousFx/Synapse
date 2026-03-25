using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.GettingStarted.Step4_Pipelines;

public static class Step4_Pipelines
{
    public static Task RunAsync(IInvoker invoker)
    {
        Console.WriteLine("─── Step 4: Pipeline Behaviors ───\n");
        Console.WriteLine("(Pipeline behaviors are demonstrated in other steps)\n");
        return Task.CompletedTask;
    }
}
