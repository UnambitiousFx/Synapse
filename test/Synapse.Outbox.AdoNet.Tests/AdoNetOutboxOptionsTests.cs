using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse.Outbox.AdoNet.Tests;

[TestSubject(typeof(AdoNetOutboxOptions))]
public sealed class AdoNetOutboxOptionsTests
{
    [Theory]
    [InlineData("outbox; DROP TABLE users", "outbox_events")]
    [InlineData("outbox", "outbox events")]
    [InlineData("outbox", "1outbox")]
    [InlineData("", "outbox_events")]
    public void AddAdoNetEventOutbox_WithANameThatIsNotAPlainIdentifier_Throws(string schema, string table)
    {
        // Arrange (Given) — names are embedded in SQL, so anything but a plain identifier is refused up front
        var services = new ServiceCollection();

        // Act (When)
        var exception = Record.Exception(() => services.AddAdoNetEventOutbox(options =>
        {
            options.Schema = schema;
            options.Table = table;
        }));

        // Assert (Then)
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void GetCreateTableScript_ForPostgreSql_QuotesTheQualifiedTableName()
    {
        // Arrange (Given)
        var options = new AdoNetOutboxOptions { Dialect = OutboxSqlDialect.PostgreSql, Schema = "app", Table = "events" };

        // Act (When)
        var script = options.GetCreateTableScript();

        // Assert (Then)
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"app\".\"events\"", script);
    }

    [Fact]
    public void GetCreateTableScript_ForSqlServer_QuotesTheQualifiedTableName()
    {
        // Arrange (Given)
        var options = new AdoNetOutboxOptions { Dialect = OutboxSqlDialect.SqlServer, Schema = "app", Table = "events" };

        // Act (When)
        var script = options.GetCreateTableScript();

        // Assert (Then)
        Assert.Contains("CREATE TABLE [app].[events]", script);
    }
}
