using UnambitiousFx.Examples.Application.Domain.Entities;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.Application.Domain.Events;

public sealed record TodoCreated : IEvent
{
    public required Todo Todo { get; init; }
}