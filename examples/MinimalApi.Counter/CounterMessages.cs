using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Counter;

// ═══════════════════════════════════════════════════════════════
// Counter feature messages — every response is a bare value type (int).
// These exercise the known-issue 001 path (value-type responses through closed CQRS + open-generic
// [PipelineBehavior]) from a SEPARATE assembly that the host composes via its generated RegisterGroup.
// ═══════════════════════════════════════════════════════════════

/// <summary>Increments the counter and returns the new value.</summary>
public sealed record IncrementCounterCommand : IRequest<int>;

/// <summary>Returns the current counter value.</summary>
public sealed record GetCounterQuery : IRequest<int>;

/// <summary>
///     Intentionally illegal command: its handler sends another request from within the handler, crossing the
///     CQRS boundary. Used to demonstrate that <c>CqrsBoundaryEnforcementBehavior</c> (emitted as a closed
///     registration by this assembly's generator) is genuinely wired into the host pipeline.
/// </summary>
public sealed record IllegalNestedCommand : IRequest<int>;
