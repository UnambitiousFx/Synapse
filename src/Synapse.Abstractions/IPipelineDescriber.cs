using System.Diagnostics.CodeAnalysis;

namespace UnambitiousFx.Synapse.Abstractions;

/// <summary>
///     Reports the pipeline a request or event type resolves to, so a test can assert on it, for example that every
///     request traverses the security behaviors.
/// </summary>
/// <remarks>
///     Describing resolves the behaviors from a fresh DI scope, because <see cref="IOrderedPipelineBehavior.Order" />
///     is an instance property. A behavior whose constructor needs something that only exists inside a real request
///     can therefore throw here, and a handler that only implements <see cref="IAsyncDisposable" /> makes disposing
///     the scope throw.
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
    ///     request in an assembly. Picks <see cref="IRequest" /> or <see cref="IRequest{TResponse}" /> from the type.
    /// </summary>
    /// <param name="requestType">A type implementing <see cref="IRequest" /> or <see cref="IRequest{TResponse}" />.</param>
    /// <returns>The description, or <c>null</c> when no handler is registered for the request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestType" /> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="requestType" /> is not a request type.</exception>
    /// <remarks>
    ///     Builds a generic method at runtime, so it is not Native-AOT safe. Use the generic overloads in an AOT
    ///     application; this one is meant for tests.
    /// </remarks>
    [RequiresDynamicCode("Builds a generic method over the request type at runtime. Use the generic overloads under Native AOT.")]
    [RequiresUnreferencedCode("Looks up the generic overloads by reflection. Use the generic overloads when trimming.")]
    PipelineDescription? Describe(Type requestType);

    /// <summary>
    ///     Describes the pipeline of an event known only as a <see cref="Type" />. See <see cref="Describe(Type)" />.
    /// </summary>
    /// <param name="eventType">A reference type implementing <see cref="IEvent" />.</param>
    /// <returns>The description, or <c>null</c> when no handler is subscribed to the event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventType" /> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="eventType" /> is not an event type.</exception>
    [RequiresDynamicCode("Builds a generic method over the event type at runtime. Use the generic overloads under Native AOT.")]
    [RequiresUnreferencedCode("Looks up the generic overloads by reflection. Use the generic overloads when trimming.")]
    PipelineDescription? DescribeEvent(Type eventType);
}
