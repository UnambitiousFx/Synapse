using System.Data;
using System.Data.Common;
using JetBrains.Annotations;

namespace UnambitiousFx.Synapse.Outbox.AdoNet.Tests;

[TestSubject(typeof(AdoNetOutboxTransaction))]
public sealed class AdoNetOutboxTransactionTests
{
    [Fact]
    public void Enlist_WithAnOpenTransaction_MakesItCurrent()
    {
        // Arrange (Given)
        var holder = new AdoNetOutboxTransaction();
        var transaction = new FakeTransaction(hasConnection: true);

        // Act (When)
        holder.Enlist(transaction);

        // Assert (Then)
        Assert.Same(transaction, holder.Current);
    }

    [Fact]
    public void Enlist_WithACompletedTransaction_Throws()
    {
        // Arrange (Given) — a committed or rolled-back transaction no longer has a connection
        var holder = new AdoNetOutboxTransaction();

        // Act (When)
        var exception = Record.Exception(() => holder.Enlist(new FakeTransaction(hasConnection: false)));

        // Assert (Then)
        Assert.IsType<ArgumentException>(exception);
        Assert.Null(holder.Current);
    }

    [Fact]
    public void Enlist_WithNull_Throws()
    {
        // Arrange (Given)
        var holder = new AdoNetOutboxTransaction();

        // Act (When)
        var exception = Record.Exception(() => holder.Enlist(null!));

        // Assert (Then)
        Assert.IsType<ArgumentNullException>(exception);
    }

    [Fact]
    public void Clear_AfterEnlist_StopsEnlisting()
    {
        // Arrange (Given)
        var holder = new AdoNetOutboxTransaction();
        holder.Enlist(new FakeTransaction(hasConnection: true));

        // Act (When)
        holder.Clear();

        // Assert (Then)
        Assert.Null(holder.Current);
    }

    private sealed class FakeTransaction(bool hasConnection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection? DbConnection => hasConnection ? new FakeConnection() : null;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }

    private sealed class FakeConnection : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => string.Empty;
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotSupportedException();
        }

        protected override DbCommand CreateDbCommand()
        {
            throw new NotSupportedException();
        }
    }
}
