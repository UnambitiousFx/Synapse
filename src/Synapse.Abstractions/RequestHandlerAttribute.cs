namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Specifies a class as a request handler for a request implementing <see cref="IRequest" />.
///     This attribute is intended to associate a type with handling a specific request type.
/// </summary>
/// <typeparam name="TRequest">
///     The type of the request being handled. The request type must implement <see cref="IRequest" />.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequestHandlerAttribute<TRequest> : Attribute
    where TRequest : IRequest
{
}

/// <summary>
///     Represents an attribute used to indicate that a class is a handler for a specific request type.
///     This attribute is intended to be applied to classes that handle requests implementing the <see cref="IRequest" />
///     interface.
/// </summary>
/// <typeparam name="TRequest">
///     The type of the request that the attributed handler will process.
///     Must implement the <see cref="IRequest" /> interface.
/// </typeparam>
/// <typeparam name="TResponse"></typeparam>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequestHandlerAttribute<TRequest, TResponse> : Attribute
    where TRequest : IRequest<TResponse>
    where TResponse : notnull
{
}