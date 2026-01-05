using UnambitiousFx.Examples.Application.Domain.Entities;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Domain.Events;

public sealed record TodoDeleted : IEvent
{
    public required Todo Todo { get; init; }
}