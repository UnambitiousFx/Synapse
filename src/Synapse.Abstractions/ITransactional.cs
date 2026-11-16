namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Marks a request whose handler must run inside a transaction. The transaction behavior registered with
///     <c>AddTransactions&lt;TManager&gt;()</c> opens an <see cref="ITransactionScope" /> around the handler, commits
///     it when the handler returns a successful <c>Result</c>, and rolls it back when the handler returns a failure
///     or throws.
/// </summary>
/// <remarks>
///     A pure opt-in marker, usually combined with <see cref="ICommand" /> or <see cref="ICommand{TResponse}" />:
///     <c>public sealed record CloseEnvelope(Guid Id) : ICommand, ITransactional;</c>. <c>AddTransactions</c> registers the behavior
///     only for <c>ITransactional</c> requests; with <c>[assembly: SynapseGlobalBehavior(typeof(TransactionBehavior&lt;,&gt;))]</c>
///     other requests reach it too and pass straight through.
/// </remarks>
public interface ITransactional;
