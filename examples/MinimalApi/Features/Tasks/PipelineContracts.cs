namespace UnambitiousFx.Examples.MinimalApi.Features.Tasks;

// ═══════════════════════════════════════════════════════════════
// Pipeline Behavior Contracts
//
// Marker interfaces used as generic constraints in pipeline behaviors.
// Attaching one of these to a request/command opts it into the
// corresponding cross-cutting concern automatically — no explicit
// behavior registration required per request type.
// ═══════════════════════════════════════════════════════════════

/// <summary>
///     Marks a command as auditable.
///     Any request implementing this interface will automatically be wrapped
///     by <c>AuditBehavior&lt;TRequest&gt;</c> via the source-generated <c>RegisterGroup</c>.
///     Queries and read-only requests should NOT implement this — the constraint
///     keeps the behavior out of the query pipeline entirely.
/// </summary>
public interface IAuditableRequest
{
}

/// <summary>
///     Marks a request as requiring an explicit permission.
///     <c>AuthorizationBehavior&lt;TRequest&gt;</c> (registered as a runtime open-generic)
///     reads <see cref="RequiredPermission" /> and checks it against the
///     <c>X-User-Permissions</c> request header, short-circuiting the pipeline
///     on a mismatch without ever invoking the downstream handler.
/// </summary>
public interface ISecuredRequest
{
    /// <summary>The permission string that must be present in the caller's permission set.</summary>
    string RequiredPermission { get; }
}
