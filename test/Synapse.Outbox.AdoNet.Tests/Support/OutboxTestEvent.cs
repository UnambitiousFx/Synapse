using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Outbox.AdoNet.Tests.Support;

public sealed record OutboxTestEvent(string Name) : IEvent;
