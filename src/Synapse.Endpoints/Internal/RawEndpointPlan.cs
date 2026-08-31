using Microsoft.AspNetCore.Builder;

namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     The startup-time half of an endpoint: where it lives on the route table and what metadata it
///     declares. This is <see cref="EndpointDescriptor" /> minus <c>InvokeAsync</c> — the request-time
///     half, which every endpoint supplies uniformly through <c>RawEndpoint.HandleAsync</c>.
/// </summary>
/// <remarks>
///     Exists so that <c>RawEndpoint.CreateDescriptor</c> can be sealed: each endpoint shape
///     contributes only its own route resolution and metadata through <c>CreatePlan</c>, and the one
///     sealed method turns that plus <c>HandleAsync</c> into the single descriptor the library maps.
/// </remarks>
internal sealed class RawEndpointPlan
{
    public required string Route { get; init; }

    public required string[] HttpMethods { get; init; }

    public required Action<RouteHandlerBuilder> ApplyMetadata { get; init; }
}
