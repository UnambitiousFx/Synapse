namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Handles a <see cref="ICommand" />. A naming alias of <see cref="IRequestHandler{TRequest}" />: registration,
///     the source generator and the analyzers treat it exactly like any request handler.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand>
    where TCommand : ICommand;

/// <summary>
///     Handles a <see cref="ICommand{TResponse}" />. A naming alias of
///     <see cref="IRequestHandler{TRequest, TResponse}" />: registration, the source generator and the analyzers treat
///     it exactly like any request handler.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
    where TResponse : notnull;
