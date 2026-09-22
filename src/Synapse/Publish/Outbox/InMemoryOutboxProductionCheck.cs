using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Publish.Outbox;

/// <summary>
///     Warns at host startup when the in-memory outbox storage is running in a Production environment.
/// </summary>
/// <remarks>
///     Only a warning, not a failure: the in-memory storage is a legitimate choice for a single-process app that
///     accepts losing pending events on restart, so refusing to start would break those users. It is checked at
///     startup rather than at registration so that a storage swapped in through DI after
///     <c>AddSynapse</c> is taken into account. Apps that run without a generic host never start hosted services
///     and so never see it. <see cref="IEventOutboxStorage" /> is now resolved through a short-lived scope rather
///     than injected directly: a custom storage can be registered Scoped (e.g. backed by a <c>DbContext</c>), and
///     this hosted service is itself a Singleton, so a direct constructor dependency would be a captive-dependency
///     violation under <c>ValidateScopes</c>.
/// </remarks>
internal sealed class InMemoryOutboxProductionCheck : IHostedService
{
    private readonly IHostEnvironment? _environment;
    private readonly ILogger<InMemoryOutboxProductionCheck> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public InMemoryOutboxProductionCheck(IServiceScopeFactory scopeFactory,
        ILogger<InMemoryOutboxProductionCheck> logger,
        IHostEnvironment? environment = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _environment = environment;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_environment?.IsProduction() != true)
        {
            return Task.CompletedTask;
        }

        using var scope = _scopeFactory.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IEventOutboxStorage>();

        if (storage is InMemoryEventOutboxStorage)
        {
            _logger.LogWarning(
                "The in-memory outbox storage is registered in a Production environment. It is not enlisted in your " +
                "database transaction: pending events are lost on restart, and events emitted by a command that then " +
                "failed can be dispatched by a later commit unless OutboxDiscardOnFailureBehavior is wired. " +
                "Register a transactional IEventOutboxStorage for production");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
