using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using UnambitiousFx.Synapse.Outbox.EntityFrameworkCore;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests;

[TestSubject(typeof(OutboxDbContext))]
public sealed class OutboxEntityTypeConfigurationTests
{
    [Fact]
    public async Task OutboxDbContext_AddAndSaveEntity_PersistsAndReadsBack()
    {
        // Arrange (Given)
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new OutboxDbContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var entity = new OutboxEntity
        {
            Id = Guid.NewGuid(),
            EventType = "System.String",
            Payload = "\"hello\"",
            Headers = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act (When)
        context.OutboxEvents.Add(entity);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert (Then)
        var reloaded = await context.OutboxEvents
            .AsNoTracking()
            .SingleAsync(e => e.Id == entity.Id, TestContext.Current.CancellationToken);
        Assert.Equal(entity.Payload, reloaded.Payload);

        await connection.CloseAsync();
    }
}
