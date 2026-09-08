using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.AspNetCore.Http;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Builders;
using UnambitiousFx.Synapse.Endpoints.Internal;

// UnambitiousFx.Functional declares an IResult of its own, so the unqualified name has to be
// pinned to ASP.NET's — the same device Synapse.AspNetCore's IHttpInvoker uses.
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     The middle level for a command with no response: you write the binding, the base class
///     dispatches <typeparamref name="TRequest" />. Responds with <c>204 No Content</c> unless
///     configured otherwise.
/// </summary>
/// <typeparam name="TRequest">The command to dispatch.</typeparam>
/// <remarks>
///     See <see cref="BoundEndpoint{TRequest,TResponse}" />; this is the same level for the arity with no
///     response body.
/// </remarks>
public abstract class BoundEndpoint<TRequest> : EndpointLifecycle<TRequest>
    where TRequest : IRequest
{
    private EndpointConfiguration<Unit>? _configuration;

    /// <summary>Configures the endpoint. Called once at startup.</summary>
    /// <param name="builder">The endpoint builder.</param>
    public virtual void Configure(IEndpointBuilder builder)
    {
    }

    /// <summary>Maps a successful dispatch to an HTTP result.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The HTTP result to write.</returns>
    public virtual IResult OnSuccess(HttpContext context)
    {
        return TypedResults.NoContent();
    }

    /// <inheritdoc />
    private protected sealed override ValueTask<IResult> ProduceResultAsync(TRequest bound,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var configuration = Mapped(_configuration);
        var invoker = context.RequestServices.GetRequiredService<IHttpInvoker>();

        // The onSuccess factory is only invoked by the invoker when the dispatch actually
        // succeeds; a mapped failure is written as-is and never reaches it.
        return invoker.InvokeAsync(
            bound,
            () => configuration.SuccessMapper is not null
                ? configuration.SuccessMapper(default)
                : OnSuccess(context),
            cancellationToken);
    }

    internal override RawEndpointPlan CreatePlan(EndpointMetadata metadata)
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
