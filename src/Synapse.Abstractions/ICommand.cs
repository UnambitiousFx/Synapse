namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Marks a request that changes state and produces no response. A pure intent marker: the library treats it like
///     any <see cref="IRequest" />, and it lets a behavior be scoped to commands with a generic constraint
///     (<c>where TRequest : ICommand</c>).
/// </summary>
public interface ICommand : IRequest;

/// <summary>
///     Marks a request that changes state and produces a response, such as the new entity's identifier. A pure intent
///     marker: the library treats it like any <see cref="IRequest{TResponse}" />, and it lets a behavior be scoped to
///     commands with a generic constraint (<c>where TRequest : ICommand&lt;TResponse&gt;</c>).
/// </summary>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>;
