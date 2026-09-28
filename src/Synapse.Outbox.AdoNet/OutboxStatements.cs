namespace UnambitiousFx.Synapse.Outbox.AdoNet;

/// <summary>
///     The SQL statements <see cref="AdoNetEventOutboxStorage" /> runs, built once from its options.
/// </summary>
internal sealed class OutboxStatements
{
    /// <summary>The columns every entry-returning statement selects, in the order the reader expects them.</summary>
    internal const string EntryColumns = "id, event_type, payload, headers, created_at";

    public OutboxStatements(AdoNetOutboxOptions options)
    {
        options.Validate();
        var dialect = options.Dialect;
        var table = $"{dialect.Quote(options.Schema)}.{dialect.Quote(options.Table)}";
        var pending = $"processed = {dialect.False} AND dead_letter = {dialect.False} AND discarded = {dialect.False}";
        var claimable = $"{pending} AND (next_attempt_at IS NULL OR next_attempt_at <= @now) " +
                        "AND (claimed_until IS NULL OR claimed_until <= @now)";

        Insert = $"""
                  INSERT INTO {table} (id, event_type, payload, headers, created_at, processed, dead_letter, discarded, attempts)
                  VALUES (@id, @event_type, @payload, @headers, @created_at, {dialect.False}, {dialect.False}, {dialect.False}, 0)
                  """;
        SelectClaimable = $"SELECT {EntryColumns} FROM {table} WHERE {claimable} ORDER BY created_at, id";
        Claim = dialect.GetClaimSql(table, claimable, EntryColumns, limited: false);
        ClaimLimited = dialect.GetClaimSql(table, claimable, EntryColumns, limited: true);
        MarkProcessed = $"""
                         UPDATE {table}
                         SET processed = {dialect.True}, processed_at = @now, last_error = NULL, next_attempt_at = NULL,
                             claimed_until = NULL, claim_token = NULL
                         WHERE id = @id
                         """;
        MarkFailed = $"""
                      UPDATE {table}
                      SET attempts = attempts + 1, last_error = @last_error, next_attempt_at = @next_attempt_at,
                          claimed_until = NULL, claim_token = NULL
                      WHERE id = @id
                      """;
        MarkDeadLetter = $"""
                          UPDATE {table}
                          SET attempts = attempts + 1, last_error = @last_error, dead_letter = {dialect.True},
                              next_attempt_at = NULL, claimed_until = NULL, claim_token = NULL
                          WHERE id = @id
                          """;
        SelectDeadLetter =
            $"SELECT {EntryColumns} FROM {table} WHERE dead_letter = {dialect.True} ORDER BY created_at, id";
        SelectAttempts = $"SELECT attempts FROM {table} WHERE id = @id";
        CountPending = $"SELECT COUNT(*) FROM {table} WHERE {pending}";
        CountRetrying = $"SELECT COUNT(*) FROM {table} WHERE {pending} AND attempts > 0";
        CountDeadLetter = $"SELECT COUNT(*) FROM {table} WHERE dead_letter = {dialect.True}";
        OldestPending = $"SELECT MIN(created_at) FROM {table} WHERE {pending}";
        Clear = $"DELETE FROM {table}";
        DiscardPrefix = $"""
                         UPDATE {table} SET discarded = {dialect.True}
                         WHERE processed = {dialect.False} AND dead_letter = {dialect.False} AND id IN
                         """;
    }

    public string Insert { get; }
    public string SelectClaimable { get; }
    public string Claim { get; }
    public string ClaimLimited { get; }
    public string MarkProcessed { get; }
    public string MarkFailed { get; }
    public string MarkDeadLetter { get; }
    public string SelectDeadLetter { get; }
    public string SelectAttempts { get; }
    public string CountPending { get; }
    public string CountRetrying { get; }
    public string CountDeadLetter { get; }
    public string OldestPending { get; }
    public string Clear { get; }

    /// <summary>
    ///     The discard statement up to its <c>IN</c> list, which is completed with one parameter per id.
    /// </summary>
    public string DiscardPrefix { get; }
}
