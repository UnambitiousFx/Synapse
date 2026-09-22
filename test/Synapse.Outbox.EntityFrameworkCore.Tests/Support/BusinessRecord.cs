namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

public sealed class BusinessRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
