using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.MinimalApi.Features.Tasks;

// ═══════════════════════════════════════════════════════════════
// Commands - Operations that modify state
//
// All mutating commands implement IAuditableRequest so that
// AuditBehavior<T> is automatically wired by the source generator.
// Queries deliberately do NOT implement this interface — keeping
// the audit behavior out of the read path entirely.
// ═══════════════════════════════════════════════════════════════

/// <summary>
///     Command to create a new task.
///     Implements <see cref="IAuditableRequest" /> → wrapped by AuditBehavior (Order = 20).
/// </summary>
public sealed record CreateTaskCommand : IRequest<CreateTaskResult>, IAuditableRequest
{
    public required string Title { get; init; }
    public required string Description { get; init; }
}

/// <summary>
///     Result returned by <see cref="CreateTaskCommand" />.
///     Using a class (not bare <c>Guid</c>) keeps the response type a reference type.
///     This is required for runtime open-generic behavior registrations (e.g.
///     <c>CqrsBoundaryEnforcementBehavior&lt;,&gt;</c>) to satisfy .NET's AOT DI validation,
///     which rejects open-generic service closures whose type arguments are value types.
/// </summary>
public sealed record CreateTaskResult
{
    public required Guid TaskId { get; init; }
}

/// <summary>
///     Command to update an existing task.
///     Implements <see cref="IAuditableRequest" /> → wrapped by AuditBehavior (Order = 20).
/// </summary>
public sealed record UpdateTaskCommand : IRequest, IAuditableRequest
{
    public required Guid TaskId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
}

/// <summary>
///     Command to complete a task.
///     Implements <see cref="IAuditableRequest" /> → wrapped by AuditBehavior (Order = 20).
/// </summary>
public sealed record CompleteTaskCommand : IRequest, IAuditableRequest
{
    public required Guid TaskId { get; init; }
}

/// <summary>
///     Command to delete a task.
///     Implements <see cref="IAuditableRequest" /> → wrapped by AuditBehavior (Order = 20).
/// </summary>
public sealed record DeleteTaskCommand : IRequest, IAuditableRequest
{
    public required Guid TaskId { get; init; }
}

/// <summary>
///     Admin command that removes all completed tasks.
///     Implements <see cref="ISecuredRequest" /> → protected by AuthorizationBehavior
///     (runtime open-generic, short-circuits without <c>tasks:admin</c> permission).
///     Does NOT implement <see cref="IAuditableRequest" /> — demonstrating selective
///     behavior targeting: the audit trail is not applied to admin bulk operations.
/// </summary>
public sealed record PurgeCompletedTasksCommand : IRequest<PurgeResult>, ISecuredRequest
{
    public string RequiredPermission => "tasks:admin";
}

/// <summary>
///     Result returned by <see cref="PurgeCompletedTasksCommand" />.
///     Using a class (not <c>int</c>) keeps the response type a reference type,
///     which is required for runtime open-generic DI resolution under .NET Native AOT validation.
/// </summary>
public sealed record PurgeResult
{
    public required int PurgedCount { get; init; }
}