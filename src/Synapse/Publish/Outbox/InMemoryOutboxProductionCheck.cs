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
///     and so never see it.
/// </remarks>
internal sealed class InMemoryOutboxProductionCheck : IHostedService
{
    private readonly IHostEnvironment? _environment;
    private readonly ILogger<InMemoryOutboxProductionCheck> _logger;
    private readonly IEventOutboxStorage _storage;

    public InMemoryOutboxProductionCheck(IEventOutboxStorage storage,
        ILogger<InMemoryOutboxProductionCheck> logger,
        IHostEnvironment? environment = null)
    {
        _storage = storage;
        _logger = logger;
        _environment = environment;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_storage is InMemoryEventOutboxStorage && _environment?.IsProduction() == true)
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
