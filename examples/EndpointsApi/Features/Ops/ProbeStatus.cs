using Microsoft.AspNetCore.Http;
using UnambitiousFx.Examples.EndpointsApi.Infrastructure;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Endpoints;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Ops;

/// <summary>
///     The request for one probe. Deliberately not an <c>IRequest&lt;T&gt;</c>: there is no message
///     here, and the self-handled tier does not ask for one.
/// </summary>
/// <param name="Probe">The probe name, bound from the route.</param>
public sealed record ProbeQuery(string Probe);

/// <summary>One probe's answer.</summary>
/// <param name="Probe">The probe that was asked.</param>
/// <param name="Healthy">Whether it is healthy.</param>
public sealed record ProbeDto(string Probe, bool Healthy);

/// <summary>
///     The self-handled tier: the generated binder and the declarative responses of the high level,
///     but the endpoint answers the request itself instead of dispatching a message.
/// </summary>
/// <remarks>
///     <para>
///         Compare the three neighbours. <see cref="HealthEndpoint" /> drops to <c>BoundEndpoint</c>
///         because its answer is a decision about the HTTP exchange (an <c>If-None-Match</c>
///         negotiation), so there is nothing to bind. <c>TagReportEndpoint</c> keeps the mediator
///         because its query is a real message with a handler. This one is neither: it has a request
///         worth binding and three lines of logic behind it, and a message type plus a handler plus a
///         registration would all exist only to hand back a value the endpoint can compute.
///     </para>
///     <para>
///         The cost is that nothing wraps <see cref="ExecuteAsync" /> — no pipeline behaviour, so no
///         validation stage, no retries, no outbox. A projection over an in-memory store needs none of
///         them; anything that does has a domain message and belongs on
///         <see cref="Endpoint{TRequest,TResponse}" />.
///     </para>
/// </remarks>
[Get("/ops/probes/{probe}")]
public sealed partial class ProbeEndpoint : InlineEndpoint<ProbeQuery, ProbeDto>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder<ProbeDto> builder)
    {
        builder.Ok()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .Tag("Ops")
            .Summary("One probe's health");
    }

    /// <inheritdoc />
    public override ValueTask<Result<ProbeDto>> ExecuteAsync(ProbeQuery request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        // Endpoints are startup singletons, so dependencies come off the request rather than the
        // constructor — exactly as they do on every other tier.
        var repository = context.Service<TaskRepository>();

        return ValueTask.FromResult(request.Probe switch
        {
            "tasks" => Result.Success(new ProbeDto("tasks", repository.GetAll().Count >= 0)),
            "store" => Result.Success(new ProbeDto("store", true)),

            // Mapped by the registered IFailureHttpMapper, so this 404 is the same 404 a handler's
            // NotFoundFailure produces on the dispatching tiers.
            _ => Result.Failure<ProbeDto>(new NotFoundFailure("Probe", request.Probe))
        });
    }
}
