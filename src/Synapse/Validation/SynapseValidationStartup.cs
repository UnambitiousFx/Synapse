using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Validation;

/// <summary>
///     Runs <c>ValidateSynapse</c> when the host starts; registered by <c>ValidateOnStart()</c>.
/// </summary>
/// <remarks>
///     Validation resolves every pipeline. When the dependency-injection container finds too little stack left, it
///     continues the resolution on a thread-pool thread while still holding its resolution lock, and a factory that
///     resolves another service from there deadlocks (seen under Native AOT on linux-musl, issue #221). A dedicated
///     thread with a large stack keeps the container from taking that path. Thread-pool threads are no fix: their
///     stacks are smaller than the main thread's.
/// </remarks>
internal sealed class SynapseValidationStartup(IServiceProvider services, ILogger<SynapseValidationStartup> logger)
    : IHostedService
{
    private const string ThreadName = "Synapse start-up validation";
    private const int StackSize = 16 * 1024 * 1024;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var completion =
            new TaskCompletionSource<SynapseValidationReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(services.ValidateSynapse());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, StackSize)
        {
            IsBackground = true,
            Name = ThreadName
        };
        thread.Start();

        // On cancellation the background thread finishes on its own; nothing waits for it.
        var report = await completion.Task.WaitAsync(cancellationToken);
        foreach (var warning in report.Warnings)
        {
            logger.LogWarning("{Code}: {Message}", warning.Code, warning.Message);
        }

        report.ThrowIfInvalid();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
