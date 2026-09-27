using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

/// <summary>
///     Records the SQL of every reader command a context executes, so a test can assert what was
///     evaluated by the database rather than in memory.
/// </summary>
public sealed class CommandTextRecorder : DbCommandInterceptor
{
    private readonly List<string> _commands = [];

    public IReadOnlyList<string> Commands => _commands;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _commands.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
