using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Outbox.EntityFrameworkCore.Tests.Support;

public sealed record OutboxTestEvent(string Name) : IEvent;
