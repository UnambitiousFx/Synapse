namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Marks a request that reads state and always produces data. A pure intent marker: the library treats it like any
///     <see cref="IRequest{TResponse}" />, and it lets a behavior be scoped to queries with a generic constraint
///     (<c>where TRequest : IQuery&lt;TResponse&gt;</c>).
/// </summary>
/// <typeparam name="TResponse">The type of the data returned.</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>;
