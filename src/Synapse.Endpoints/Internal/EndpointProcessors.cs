using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     The pre- and post-processors one endpoint registered, resolved at request time.
/// </summary>
/// <remarks>
///     Each entry is a resolver closure rather than a <see cref="Type" />, so resolution goes through
///     the generic <c>GetRequiredService&lt;T&gt;</c> and stays free of anything the trimmer has to
///     reason about. Both <c>Run…Async</c> methods return without entering an async state machine when
///     nothing is registered, which is what keeps an endpoint that uses no processors paying nothing
///     for the feature.
/// </remarks>
internal sealed class EndpointProcessors
{
    /// <summary>The instance shared by every endpoint that registered no processors.</summary>
    internal static readonly EndpointProcessors Empty = new([], []);

    private readonly Func<HttpContext, IEndpointPreProcessor>[] _pre;
    private readonly Func<HttpContext, IEndpointPostProcessor>[] _post;

    internal EndpointProcessors(Func<HttpContext, IEndpointPreProcessor>[] pre,
        Func<HttpContext, IEndpointPostProcessor>[] post)
    {
        _pre = pre;
        _post = post;
    }

    /// <summary>Runs each pre-processor in registration order until one answers the request.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>The first non-null result, or <see langword="null" /> to carry on.</returns>
    internal ValueTask<IResult?> RunPreAsync(HttpContext context,
        CancellationToken cancellationToken)
    {
        return _pre.Length == 0 ? default : RunPreCoreAsync(context, cancellationToken);
    }

    /// <summary>Folds the result through each post-processor in registration order.</summary>
    /// <param name="result">The result the endpoint produced.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token, tied to the request.</param>
    /// <returns>The result to write.</returns>
    internal ValueTask<IResult> RunPostAsync(IResult result,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        return _post.Length == 0
            ? new ValueTask<IResult>(result)
            : RunPostCoreAsync(result, context, cancellationToken);
    }

    private async ValueTask<IResult?> RunPreCoreAsync(HttpContext context,
        CancellationToken cancellationToken)
    {
        foreach (var resolve in _pre)
        {
            var result = await resolve(context)
                .ProcessAsync(context, cancellationToken);

            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private async ValueTask<IResult> RunPostCoreAsync(IResult result,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        foreach (var resolve in _post)
        {
            var processor = resolve(context);

            // Named here rather than left to the endpoint-level null guard: that one reports the
            // endpoint, which does not say which of several registered processors dropped the result.
            result = await processor.ProcessAsync(result, context, cancellationToken)
                     ?? throw new InvalidOperationException(
                         $"Post-processor '{processor.GetType()}' returned a null result. Return the " +
                         "result it was given to leave the response unchanged, or a replacement to " +
                         "change it.");
        }

        return result;
    }
}
