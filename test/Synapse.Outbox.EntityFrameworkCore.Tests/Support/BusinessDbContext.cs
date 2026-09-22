using Microsoft.EntityFrameworkCore;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

public sealed class BusinessDbContext(DbContextOptions<BusinessDbContext> options) : DbContext(options)
{
    public DbSet<BusinessRecord> Records => Set<BusinessRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BusinessRecord>(b =>
        {
            b.ToTable("business_records");
            b.HasKey(r => r.Id);
        });
    }
}
