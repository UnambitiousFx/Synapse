using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.GettingStarted.Step6_ErrorHandling;

public static class Step6_ErrorHandling
{
    public static Task RunAsync(IInvoker invoker)
    {
        Console.WriteLine("─── Step 6: Error Handling with Result Types ───\n");
        Console.WriteLine("(Error handling is demonstrated throughout the examples)\n");
        return Task.CompletedTask;
    }
}
