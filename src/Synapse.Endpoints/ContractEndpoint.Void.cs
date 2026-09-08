using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Builders;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     An endpoint whose HTTP contract is separate from the command it dispatches, for a command with
///     no response. Responds with <c>204 No Content</c> unless configured otherwise.
/// </summary>
/// <typeparam name="THttpRequest">The HTTP request DTO, bound from the request.</typeparam>
/// <typeparam name="TRequest">The command dispatched through Synapse.</typeparam>
/// <remarks>
///     <para>
///         See <see cref="ContractEndpoint{THttpRequest,TRequest,TResponse,THttpResponse}" />; this is
///         the same tier for the arity with no response body, so it asks for
///         <see cref="ToRequest" /> and nothing else. Without it, a command with a wire contract of
///         its own had to borrow the four-argument tier and invent a response type for a <c>204</c>
///         that never carries one.
///     </para>
///     <para>
///         Prefer <see cref="Endpoint{TRequest}" /> unless the wire contract genuinely must differ
///         from the message; this variant costs one mapping method per endpoint.
///     </para>
/// </remarks>
public abstract class ContractEndpoint<THttpRequest, TRequest> : EndpointLifecycle<THttpRequest>
    where TRequest : IRequest
{
    private EndpointConfiguration<Unit>? _configuration;

    /// <summary>Configures the endpoint. Called once at startup.</summary>
    /// <param name="builder">The endpoint builder.</param>
    public virtual void Configure(IEndpointBuilder builder)
    {
    }

    /// <summary>Maps the bound HTTP request onto the CQRS command.</summary>
    /// <param name="request">The bound HTTP request DTO.</param>
    /// <returns>The command to dispatch.</returns>
    public abstract TRequest ToRequest(THttpRequest request);

    /// <summary>Maps a successful dispatch to an HTTP result.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The HTTP result to write.</returns>
    public virtual IResult OnSuccess(HttpContext context)
    {
        return TypedResults.NoContent();
    }

    /// <inheritdoc />
    private protected sealed override ValueTask<IResult> ProduceResultAsync(THttpRequest bound,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var configuration = Mapped(_configuration);
        var invoker = context.RequestServices.GetRequiredService<IHttpInvoker>();

        // The onSuccess factory is only invoked by the invoker when the dispatch actually succeeds;
        // a mapped failure is written as-is and never reaches it.
        return invoker.InvokeAsync(
            ToRequest(bound),
            () => configuration.SuccessMapper is not null
                ? configuration.SuccessMapper(default)
                : OnSuccess(context),
            cancellationToken);
    }

    internal sealed override RawEndpointPlan CreatePlan(EndpointMetadata metadata)
    {
        var builder = new EndpointBuilder<Unit>(metadata);
        Configure(builder);
        var configuration = builder.Build();
        _configuration = configuration;

        return BuildPlan(
            configuration.Route,
            configuration.HttpMethods,
            configuration.Processors,
            new ProducesResponseMetadata(configuration.SuccessStatusCode(StatusCodes.Status204NoContent)),
            configuration.ApplyMetadata);
    }
}
