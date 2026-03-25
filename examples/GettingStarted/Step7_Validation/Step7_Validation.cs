using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Examples.GettingStarted.Step7_Validation;

public static class Step7_Validation
{
    public static Task RunAsync(IServiceProvider services)
    {
        Console.WriteLine("─── Step 7: Request Validation ───\n");
        Console.WriteLine("(Validation patterns can be implemented using pipeline behaviors)\n");
        return Task.CompletedTask;
    }
}
