using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UnambitiousFx.Synapse.Validation;

/// <summary>
///     Runs <c>ValidateSynapse</c> when the host starts; registered by <c>ValidateOnStart()</c>.
/// </summary>
internal sealed class SynapseValidationStartup(IServiceProvider services, ILogger<SynapseValidationStartup> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var report = services.ValidateSynapse();
        foreach (var warning in report.Warnings)
        {
            logger.LogWarning("{Code}: {Message}", warning.Code, warning.Message);
        }

        report.ThrowIfInvalid();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
