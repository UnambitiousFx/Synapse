namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Marks a request whose handler must run inside a transaction. The transaction behavior registered with
///     <c>AddTransactions&lt;TManager&gt;()</c> opens an <see cref="ITransactionScope" /> around the handler, commits
///     it when the handler returns a successful <c>Result</c>, and rolls it back when the handler returns a failure
///     or throws.
/// </summary>
/// <remarks>
///     A pure opt-in marker, usually combined with <see cref="ICommand" /> or <see cref="ICommand{TResponse}" />:
///     <c>public sealed record CloseEnvelope(Guid Id) : ICommand, ITransactional;</c>. Requests without it never
///     reach the transaction behavior, because its generic constraint excludes them both from the dependency
///     injection container and from the source generator's registrations.
/// </remarks>
public interface ITransactional;
