namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Reports the pipeline a request or event type resolves to, so a test can assert on it, for example that every
///     request traverses the security behaviors.
/// </summary>
/// <remarks>
///     Describing resolves the handler and the behaviors from a fresh DI scope, then disposes it. The handler's
///     whole constructor dependency graph is constructed (a <c>DbContext</c>, an <c>HttpClient</c>, any warm-up side
///     effect), and the behaviors are instantiated because <see cref="IOrderedPipelineBehavior.Order" /> is an
///     instance property. A handler or behavior whose constructor needs something that only exists inside a real
///     request can therefore throw here, and a handler that only implements <see cref="IAsyncDisposable" /> makes
///     disposing the scope throw.
/// </remarks>
public interface IPipelineDescriber
{
    /// <summary>
    ///     Describes the pipeline of a request that produces no response.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <returns>The description, or <c>null</c> when no handler is registered for the request.</returns>
    PipelineDescription? Describe<TRequest>()
        where TRequest : IRequest;

    /// <summary>
    ///     Describes the pipeline of a request that produces a response.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <returns>The description, or <c>null</c> when no handler is registered for the request.</returns>
    PipelineDescription? Describe<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        where TResponse : notnull;

    /// <summary>
    ///     Describes the pipeline of an event: the behaviors that wrap the fan-out, and every handler subscribed to it.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <returns>The description, or <c>null</c> when no handler is subscribed to the event.</returns>
    PipelineDescription? DescribeEvent<TEvent>()
        where TEvent : class, IEvent;

    /// <summary>
    ///     Describes the pipeline of a request known only as a <see cref="Type" />, for tests that loop over every
    ///     request in an assembly. Served from the registrations Synapse recorded, without reflection, so it is
    ///     Native-AOT safe. When a type is registered with both <see cref="IRequest" /> and
    ///     <see cref="IRequest{TResponse}" />, <see cref="IRequest{TResponse}" /> wins; a type with several
    ///     <c>IRequest&lt;T&gt;</c> closures is ambiguous and the first registered is used.
    /// </summary>
    /// <param name="requestType">A type implementing <see cref="IRequest" /> or <see cref="IRequest{TResponse}" />.</param>
    /// <returns>
    ///     The description, or <c>null</c> when the type was not registered through Synapse (it cannot be dispatched
    ///     either) or has no handler.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestType" /> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="requestType" /> is not a request type.</exception>
    PipelineDescription? Describe(Type requestType);

    /// <summary>
    ///     Describes the pipeline of an event known only as a <see cref="Type" />. See <see cref="Describe(Type)" />.
    /// </summary>
    /// <param name="eventType">A reference type implementing <see cref="IEvent" />.</param>
    /// <returns>
    ///     The description, or <c>null</c> when the type was not registered through Synapse or no handler is
    ///     subscribed to it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventType" /> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="eventType" /> is not an event type.</exception>
    PipelineDescription? DescribeEvent(Type eventType);
}
