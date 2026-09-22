using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     Maps <see cref="OutboxEntity" /> to a table. Apply this inside your own
///     <see cref="DbContext" />'s <c>OnModelCreating</c> to keep the outbox table under your own
///     context's migrations, instead of registering the standalone <see cref="OutboxDbContext" />.
/// </summary>
/// <param name="schema">
///     The schema the outbox table is created under. Defaults to <c>"outbox"</c> so it does not
///     collide with an application's own tables regardless of which context it is applied to.
/// </param>
public sealed class OutboxEntityTypeConfiguration(string schema = "outbox")
    : IEntityTypeConfiguration<OutboxEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxEntity> builder)
    {
        builder.ToTable("outbox_events", schema);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).IsRequired();
        builder.Property(e => e.Payload).IsRequired();
        builder.Property(e => e.Headers).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.HasIndex(e => new { e.Processed, e.DeadLetter, e.Discarded, e.NextAttemptAt })
            .HasDatabaseName("ix_outbox_events_pending");
        builder.HasIndex(e => e.DeadLetter)
            .HasDatabaseName("ix_outbox_events_dead_letter");
    }
}
