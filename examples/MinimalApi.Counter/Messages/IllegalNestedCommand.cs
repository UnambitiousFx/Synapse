using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Counter.Messages;

/// <summary>
///     Intentionally illegal command: its handler sends another request from within the handler, crossing the
///     CQRS boundary. Used to demonstrate that <c>CqrsBoundaryEnforcementBehavior</c> (emitted as a closed
///     registration by this assembly's generator) is genuinely wired into the host pipeline.
/// </summary>
public sealed record IllegalNestedCommand : IRequest<int>;