using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Binding;

/// <summary>
///     Builds a message from an HTTP request. Implementations are emitted by the
///     Synapse.Endpoints analyzer, one per message type, and assign properties directly so that
///     no reflection is needed at request time.
/// </summary>
/// <typeparam name="TRequest">The message type.</typeparam>
public interface IEndpointBinder<TRequest>
{
    /// <summary>Binds the incoming request onto a new message instance.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The bound message, or a failure describing what could not be bound.</returns>
    ValueTask<BindResult<TRequest>> BindAsync(HttpContext context);
}
