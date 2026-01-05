namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Represents a marker interface that defines a request within the mediator pattern returning a response.
///     Inherits <see cref="IRequest" /> so generic requests are also considered non-generic requests.
/// </summary>
public interface IRequest<out TResponse>;

/// <summary>
///     Represents a command or query that can be sent through the mediator for processing without a response type.
///     This interface sets the base contract for requests that do not return a response.
/// </summary>
public interface IRequest;