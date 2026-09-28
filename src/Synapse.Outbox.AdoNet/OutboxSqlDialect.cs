namespace UnambitiousFx.Synapse.Outbox.AdoNet;

/// <summary>
///     The SQL dialect <see cref="AdoNetEventOutboxStorage" /> generates its statements in.
/// </summary>
/// <remarks>
///     The storage talks to the database through <c>System.Data.Common</c> only, so it has no dependency on a
///     driver: reference Npgsql or Microsoft.Data.SqlClient in your application and register its
///     <see cref="System.Data.Common.DbDataSource" />.
/// </remarks>
public abstract class OutboxSqlDialect
{
    private protected OutboxSqlDialect()
    {
    }

    /// <summary>
    ///     PostgreSQL. Claims pending entries with <c>FOR UPDATE SKIP LOCKED</c>.
    /// </summary>
    public static OutboxSqlDialect PostgreSql { get; } = new PostgreSqlDialect();

    /// <summary>
    ///     SQL Server and Azure SQL. Claims pending entries with the <c>UPDLOCK, READPAST</c> table hints.
    /// </summary>
    public static OutboxSqlDialect SqlServer { get; } = new SqlServerDialect();

    /// <summary>The literal for a true boolean column value.</summary>
    internal abstract string True { get; }

    /// <summary>The literal for a false boolean column value.</summary>
    internal abstract string False { get; }

    internal abstract string Quote(string identifier);

    internal abstract string GetCreateTableScript(string schema, string table);

    /// <summary>
    ///     Builds the statement that stamps <c>@claimed_until</c> and <c>@claim_token</c> on up to <c>@max_count</c>
    ///     claimable rows, skipping rows another transaction has locked, and returns the claimed rows' columns.
    /// </summary>
    internal abstract string GetClaimSql(string table, string claimable, string columns, bool limited);

    private sealed class PostgreSqlDialect : OutboxSqlDialect
    {
        internal override string True => "true";

        internal override string False => "false";

        public override string ToString()
        {
            return "PostgreSQL";
        }

        internal override string Quote(string identifier)
        {
            return $"\"{identifier}\"";
        }

        internal override string GetCreateTableScript(string schema, string table)
        {
            var qualified = $"{Quote(schema)}.{Quote(table)}";
            return $"""
                    CREATE SCHEMA IF NOT EXISTS {Quote(schema)};
                    CREATE TABLE IF NOT EXISTS {qualified} (
                        id uuid NOT NULL PRIMARY KEY,
                        event_type text NOT NULL,
                        payload text NOT NULL,
                        headers text NOT NULL,
                        created_at timestamptz NOT NULL,
                        processed_at timestamptz NULL,
                        processed boolean NOT NULL DEFAULT false,
                        dead_letter boolean NOT NULL DEFAULT false,
                        discarded boolean NOT NULL DEFAULT false,
                        attempts integer NOT NULL DEFAULT 0,
                        last_error text NULL,
                        next_attempt_at timestamptz NULL,
                        claimed_until timestamptz NULL,
                        claim_token uuid NULL
                    );
                    CREATE INDEX IF NOT EXISTS {Quote($"ix_{table}_pending")} ON {qualified} (created_at, id)
                        WHERE processed = false AND dead_letter = false AND discarded = false;
                    CREATE INDEX IF NOT EXISTS {Quote($"ix_{table}_dead_letter")} ON {qualified} (created_at, id)
                        WHERE dead_letter = true;
                    """;
        }

        internal override string GetClaimSql(string table, string claimable, string columns, bool limited)
        {
            // FOR UPDATE re-checks the WHERE clause against a row once its lock is taken, and SKIP LOCKED
            // passes over rows another claimer holds instead of waiting for them, so concurrent claimers
            // split the backlog between them.
            var limit = limited ? " LIMIT @max_count" : string.Empty;
            return $"""
                    UPDATE {table} SET claimed_until = @claimed_until, claim_token = @claim_token
                    WHERE id IN (
                        SELECT id FROM {table}
                        WHERE {claimable}
                        ORDER BY created_at, id{limit}
                        FOR UPDATE SKIP LOCKED)
                    RETURNING {columns}
                    """;
        }
    }

    private sealed class SqlServerDialect : OutboxSqlDialect
    {
        internal override string True => "1";

        internal override string False => "0";

        public override string ToString()
        {
            return "SQL Server";
        }

        internal override string Quote(string identifier)
        {
            return $"[{identifier}]";
        }

        internal override string GetCreateTableScript(string schema, string table)
        {
            var qualified = $"{Quote(schema)}.{Quote(table)}";
            return $"""
                    IF SCHEMA_ID(N'{schema}') IS NULL EXEC(N'CREATE SCHEMA {Quote(schema)}');
                    IF OBJECT_ID(N'{qualified}', N'U') IS NULL
                    BEGIN
                        CREATE TABLE {qualified} (
                            [id] uniqueidentifier NOT NULL CONSTRAINT {Quote($"PK_{table}")} PRIMARY KEY NONCLUSTERED,
                            [event_type] nvarchar(max) NOT NULL,
                            [payload] nvarchar(max) NOT NULL,
                            [headers] nvarchar(max) NOT NULL,
                            [created_at] datetimeoffset NOT NULL,
                            [processed_at] datetimeoffset NULL,
                            [processed] bit NOT NULL DEFAULT 0,
                            [dead_letter] bit NOT NULL DEFAULT 0,
                            [discarded] bit NOT NULL DEFAULT 0,
                            [attempts] int NOT NULL DEFAULT 0,
                            [last_error] nvarchar(max) NULL,
                            [next_attempt_at] datetimeoffset NULL,
                            [claimed_until] datetimeoffset NULL,
                            [claim_token] uniqueidentifier NULL
                        );
                        CREATE CLUSTERED INDEX {Quote($"ix_{table}_created_at")} ON {qualified} ([created_at]);
                        CREATE INDEX {Quote($"ix_{table}_pending")} ON {qualified} ([created_at], [id])
                            WHERE [processed] = 0 AND [dead_letter] = 0 AND [discarded] = 0;
                        CREATE INDEX {Quote($"ix_{table}_dead_letter")} ON {qualified} ([created_at], [id])
                            WHERE [dead_letter] = 1;
                    END;
                    """;
        }

        internal override string GetClaimSql(string table, string claimable, string columns, bool limited)
        {
            // UPDLOCK holds the selected rows until the UPDATE commits, READPAST skips rows another claimer
            // holds instead of waiting for them, and ROWLOCK keeps the locks from escalating to the page or
            // table, which would block every other claimer. ORDER BY is only legal in the CTE alongside TOP.
            var top = limited ? "TOP (@max_count) " : string.Empty;
            var orderBy = limited ? " ORDER BY created_at, id" : string.Empty;
            return $"""
                    WITH claimable AS (
                        SELECT {top}* FROM {table} WITH (UPDLOCK, READPAST, ROWLOCK)
                        WHERE {claimable}{orderBy})
                    UPDATE claimable SET claimed_until = @claimed_until, claim_token = @claim_token
                    OUTPUT {string.Join(", ", columns.Split(", ").Select(c => $"inserted.{c}"))};
                    """;
        }
    }
}
