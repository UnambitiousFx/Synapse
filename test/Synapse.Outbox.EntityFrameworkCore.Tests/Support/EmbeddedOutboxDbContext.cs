using Microsoft.EntityFrameworkCore;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

/// <summary>
///     A caller-owned <see cref="DbContext" /> that applies <see cref="OutboxEntityTypeConfiguration" />
///     next to its own unrelated entity — the "embed the outbox table in your own context" path the
///     documentation offers as an alternative to the standalone <see cref="OutboxDbContext" />.
/// </summary>
/// <remarks>
///     Deliberately separate from <see cref="BusinessDbContext" />: the transaction tests build that one
///     against a database that already has the outbox table and create its schema from
///     <c>GenerateCreateScript()</c>, which would collide if the outbox mapping were added there.
/// </remarks>
public sealed class EmbeddedOutboxDbContext(DbContextOptions<EmbeddedOutboxDbContext> options)
    : DbContext(options)
{
    public DbSet<BusinessRecord> Records => Set<BusinessRecord>();

    public DbSet<OutboxEntity> OutboxEvents => Set<OutboxEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxEntityTypeConfiguration());

        modelBuilder.Entity<BusinessRecord>(b =>
        {
            b.ToTable("business_records");
            b.HasKey(r => r.Id);
        });
    }
}
