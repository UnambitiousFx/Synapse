using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

/// <summary>
///     Maps <see cref="OutboxEntity" /> to a table. Apply this inside your own
///     <see cref="DbContext" />'s <c>OnModelCreating</c> to keep the outbox table under your own
///     context's migrations, instead of registering the standalone <see cref="OutboxDbContext" />.
/// </summary>
/// <remarks>
///     Every <see cref="DateTimeOffset" /> column is stored as a <see cref="long" /> of UTC ticks
///     (<see cref="DateTimeOffset.UtcTicks" />) and read back with a zero offset. SQLite's EF Core
///     provider cannot translate comparisons or ordering over a native <see cref="DateTimeOffset" />
///     column, and storing a plain integer lets every provider evaluate the pending-entry back-off
///     filter and ordering in SQL, without a per-provider branch.
/// </remarks>
/// <param name="schema">
///     The schema the outbox table is created under. Defaults to <c>"outbox"</c> so it does not
///     collide with an application's own tables regardless of which context it is applied to.
/// </param>
public sealed class OutboxEntityTypeConfiguration(string schema = "outbox")
    : IEntityTypeConfiguration<OutboxEntity>
{
    // Order-preserving by instant, unlike a textual or offset-carrying representation, so SQL
    // comparisons and ORDER BY agree with DateTimeOffset's own instant-based comparison.
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicksConverter = new(
        value => value.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxEntity> builder)
    {
        builder.ToTable("outbox_events", schema);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).IsRequired();
        builder.Property(e => e.Payload).IsRequired();
        builder.Property(e => e.Headers).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired().HasConversion(UtcTicksConverter);
        builder.Property(e => e.ProcessedAt).HasConversion(UtcTicksConverter);
        builder.Property(e => e.NextAttemptAt).HasConversion(UtcTicksConverter);
        builder.Property(e => e.ClaimedUntil).HasConversion(UtcTicksConverter);

        builder.HasIndex(e => new { e.Processed, e.DeadLetter, e.Discarded, e.NextAttemptAt })
            .HasDatabaseName("ix_outbox_events_pending");
        builder.HasIndex(e => e.DeadLetter)
            .HasDatabaseName("ix_outbox_events_dead_letter");
    }
}
