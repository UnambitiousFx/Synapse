using Microsoft.EntityFrameworkCore;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     Ready-made standalone <see cref="DbContext" /> holding only the outbox table. Register it with
///     your provider of choice; generate its migrations separately from your business context(s), e.g.
///     <c>dotnet ef migrations add InitialOutbox --context OutboxDbContext</c>.
/// </summary>
/// <remarks>
///     Prefer applying <see cref="OutboxEntityTypeConfiguration" /> inside your own
///     <see cref="DbContext" /> instead when you want the outbox table under your own context's
///     migration history rather than a separate one.
/// </remarks>
public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
{
    /// <summary>The stored outbox entries.</summary>
    public DbSet<OutboxEntity> OutboxEvents => Set<OutboxEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxEntityTypeConfiguration());
    }
}
