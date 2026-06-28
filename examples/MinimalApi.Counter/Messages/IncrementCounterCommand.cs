using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Counter.Messages;

// ═══════════════════════════════════════════════════════════════
// Counter feature messages — every response is a bare value type (int).
// These exercise the known-issue 001 path (value-type responses through closed CQRS + open-generic
// [PipelineBehavior]) from a SEPARATE assembly that the host composes via its generated RegisterGroup.
// ═══════════════════════════════════════════════════════════════

/// <summary>Increments the counter and returns the new value.</summary>
public sealed record IncrementCounterCommand : IRequest<int>;