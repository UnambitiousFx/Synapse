using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Counter.Messages;

/// <summary>Returns the current counter value.</summary>
public sealed record GetCounterQuery : IRequest<int>;