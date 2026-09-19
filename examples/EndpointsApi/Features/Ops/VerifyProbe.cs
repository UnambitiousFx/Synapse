using Microsoft.AspNetCore.Http;
using UnambitiousFx.Examples.EndpointsApi.Infrastructure;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Endpoints;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Ops;

/// <summary>
///     The request for one probe verification. Like <see cref="ProbeQuery" /> it is not an
///     <c>IRequest</c>: the self-handled tier asks for no message at either arity.
/// </summary>
/// <param name="Probe">The probe name, bound from the route.</param>
public sealed record VerifyProbeQuery(string Probe);

/// <summary>
///     The self-handled tier at the arity with no response body: the generated binder and the
///     declarative responses of the high level, a <c>204</c> the base class supplies, and the
///     endpoint answering the request itself instead of dispatching a message.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately next to <see cref="ProbeEndpoint" />, which takes the same input and differs
///         only in having something to say: this one configures through <see cref="IEndpointBuilder" />
///         rather than <c>IEndpointBuilder&lt;T&gt;</c>, returns the non-generic
///         <see cref="Result" />, and defaults to <c>204 No Content</c> rather than <c>200 OK</c>.
///         Comparing the two files is the fastest way to see what the second type parameter buys.
///     </para>
///     <para>
///         A <c>POST</c> whose every property comes off the route reads no body, declares no
///         <c>requestBody</c>, and accepts a request with no body at all — what decides that is the
///         binding, not the verb. The same shape as <c>ArchiveTaskEndpoint</c>, one tier up.
///     </para>
///     <para>
///         The cost is the tier's, not this route's: nothing wraps <see cref="ExecuteAsync" />, so
///         there is no validation stage, no retries and no outbox. Verifying an in-memory projection
///         needs none of them; anything that does has a domain message and belongs on
///         <see cref="Endpoint{TRequest}" />.
///     </para>
/// </remarks>
[Post("/ops/probes/{probe}/verify")]
public sealed partial class VerifyProbeEndpoint : InlineEndpoint<VerifyProbeQuery>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder builder)
    {
        // Stated explicitly even though it is the arity's default, so the OpenAPI document and the
        // source agree without a reader having to know which default applies at which arity.
        builder.NoContent()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .Tag("Ops")
            .Summary("Verify one probe, answering with its status alone");
    }

    /// <inheritdoc />
    public override ValueTask<Result> ExecuteAsync(VerifyProbeQuery request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request.Probe is "tasks")
        {
            // Endpoints are startup singletons, so dependencies come off the request rather than the
            // constructor — exactly as they do on every other tier.
            var repository = context.Service<TaskRepository>();

            // Reaching the store is the whole verification. This arity's success is the absence of a
            // failure, so the read has nothing to hand back and the caller gets the 204 — which is
            // the reason the route sits on this arity rather than beside ProbeEndpoint's.
            _ = repository.GetAll();
            return ValueTask.FromResult(Result.Success());
        }

        // Mapped by the registered IFailureHttpMapper, so this 404 is the same 404 the value arity
        // next door produces, and the same one a handler's NotFoundFailure produces on the
        // dispatching tiers.
        return ValueTask.FromResult(request.Probe is "store"
            ? Result.Success()
            : Result.Failure(new NotFoundFailure("Probe", request.Probe)));
    }
}
