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
}
