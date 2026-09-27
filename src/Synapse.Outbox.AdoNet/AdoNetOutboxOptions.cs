using System.Text.RegularExpressions;

namespace UnambitiousFx.Synapse.Outbox.AdoNet;

/// <summary>
///     Configures where <see cref="AdoNetEventOutboxStorage" /> keeps the outbox table and which SQL dialect it
///     speaks.
/// </summary>
public sealed class AdoNetOutboxOptions
{
    private static readonly Regex IdentifierPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     The SQL dialect of the database the outbox table lives in.
    /// </summary>
    public OutboxSqlDialect Dialect { get; set; } = OutboxSqlDialect.PostgreSql;

    /// <summary>
    ///     The schema the outbox table is created under. Defaults to <c>"outbox"</c>, so it never collides with an
    ///     application's own tables.
    /// </summary>
    public string Schema { get; set; } = "outbox";

    /// <summary>
    ///     The name of the outbox table. Defaults to <c>"outbox_events"</c>.
    /// </summary>
    public string Table { get; set; } = "outbox_events";

    /// <summary>
    ///     Returns the script that creates the outbox table and its indexes for these options, if they do not exist
    ///     yet. Run it from your own migration tooling, or once at startup.
    /// </summary>
    /// <returns>The DDL script for <see cref="Dialect" />.</returns>
    public string GetCreateTableScript()
    {
        Validate();
        return Dialect.GetCreateTableScript(Schema, Table);
    }

    /// <summary>
    ///     Checks the schema and table names before they are embedded in SQL: they cannot be passed as parameters,
    ///     so only plain identifiers are accepted.
    /// </summary>
    /// <exception cref="InvalidOperationException">A name is not a plain identifier.</exception>
    internal void Validate()
    {
        if (Dialect is null)
        {
            throw new InvalidOperationException($"{nameof(AdoNetOutboxOptions)}.{nameof(Dialect)} must be set.");
        }

        ValidateIdentifier(Schema, nameof(Schema));
        ValidateIdentifier(Table, nameof(Table));
    }

    private static void ValidateIdentifier(string? value, string name)
    {
        if (value is null || !IdentifierPattern.IsMatch(value))
        {
            throw new InvalidOperationException(
                $"{nameof(AdoNetOutboxOptions)}.{name} must be a plain identifier (letters, digits and " +
                $"underscores, not starting with a digit), but was '{value}'.");
        }
    }
}
