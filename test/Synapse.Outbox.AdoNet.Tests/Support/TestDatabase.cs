using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace UnambitiousFx.Synapse.Outbox.AdoNet.Tests.Support;

public enum DatabaseEngine
{
    PostgreSql,
    SqlServer
}

/// <summary>
///     A throwaway outbox table on a real database, created for one test and dropped after it.
/// </summary>
/// <remarks>
///     The connection string comes from <c>SYNAPSE_TEST_POSTGRES</c> or <c>SYNAPSE_TEST_SQLSERVER</c>. Without it
///     the test is skipped, so the suite still runs on machines without the databases; the CI job that provides
///     them also sets <c>SYNAPSE_REQUIRE_DB_TESTS=1</c>, which turns a missing connection string into a failure
///     instead, so the database tests can never silently stop running there.
/// </remarks>
public sealed class TestDatabase : IAsyncDisposable
{
    private const string Schema = "synapse_tests";

    private TestDatabase(DatabaseEngine engine, DbDataSource dataSource, AdoNetOutboxOptions options)
    {
        Engine = engine;
        DataSource = dataSource;
        Options = options;
    }

    public DatabaseEngine Engine { get; }

    public DbDataSource DataSource { get; }

    public AdoNetOutboxOptions Options { get; }

    public static async Task<TestDatabase> CreateAsync(DatabaseEngine engine, CancellationToken cancellationToken)
    {
        var variable = engine == DatabaseEngine.PostgreSql ? "SYNAPSE_TEST_POSTGRES" : "SYNAPSE_TEST_SQLSERVER";
        var connectionString = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var required = Environment.GetEnvironmentVariable("SYNAPSE_REQUIRE_DB_TESTS") == "1";
            Assert.False(required, $"{variable} must be set when SYNAPSE_REQUIRE_DB_TESTS=1.");
            Assert.Skip($"{variable} is not set.");
        }

        var dataSource = engine == DatabaseEngine.PostgreSql
            ? NpgsqlDataSource.Create(connectionString)
            : SqlClientFactory.Instance.CreateDataSource(connectionString);
        var options = new AdoNetOutboxOptions
        {
            Dialect = engine == DatabaseEngine.PostgreSql ? OutboxSqlDialect.PostgreSql : OutboxSqlDialect.SqlServer,
            Schema = Schema,
            Table = $"outbox_{Guid.NewGuid():N}"
        };

        var database = new TestDatabase(engine, dataSource, options);
        await database.ExecuteAsync(options.GetCreateTableScript(), cancellationToken);
        return database;
    }

    public AdoNetEventOutboxStorage CreateStorage(AdoNetOutboxTransaction? transaction = null)
    {
        return new AdoNetEventOutboxStorage(DataSource, transaction ?? new AdoNetOutboxTransaction(), Options);
    }

    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await DataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<object?> ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await DataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    public string QualifiedTable => Engine == DatabaseEngine.PostgreSql
        ? $"\"{Options.Schema}\".\"{Options.Table}\""
        : $"[{Options.Schema}].[{Options.Table}]";

    public async ValueTask DisposeAsync()
    {
        try
        {
            await ExecuteAsync($"DROP TABLE {QualifiedTable}", CancellationToken.None);
        }
        finally
        {
            await DataSource.DisposeAsync();
        }
    }
}
