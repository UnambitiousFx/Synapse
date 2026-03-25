using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Examples.GettingStarted.Step5_Context;

public static class Step5_Context
{
    public static Task RunAsync(IServiceProvider services)
    {
        Console.WriteLine("─── Step 5: Context and Metadata ───\n");
        Console.WriteLine("(Context features are demonstrated in advanced scenarios)\n");
        return Task.CompletedTask;
    }
}
