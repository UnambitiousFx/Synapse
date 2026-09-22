namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Handles an <see cref="IQuery{TResponse}" />. A naming alias of <see cref="IRequestHandler{TRequest, TResponse}" />:
///     registration, the source generator and the analyzers treat it exactly like any request handler.
/// </summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResponse">The type of the data returned.</typeparam>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
    where TResponse : notnull;
