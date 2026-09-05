namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     Endpoint metadata attached by <see cref="EndpointMapper.Map" /> to every endpoint this library
///     maps, so the startup duplicate-route check can tell Synapse's own endpoints apart from
///     everything else in the route table.
/// </summary>
/// <remarks>
///     Public so the opt-in <c>UnambitiousFx.Synapse.Endpoints.OpenApi</c> package can recognize
///     Synapse-mapped endpoints too, via <c>context.Description.ActionDescriptor.EndpointMetadata</c>.
///     <c>InternalsVisibleTo</c> covers only this assembly's own test project, and matching on the
///     payload metadata types (<c>BoundParametersMetadata</c>/<c>FormRequestMetadata</c>) instead would
///     silently skip a Synapse endpoint that declares no parameters. One shared
///     <see cref="Instance" /> because the marker carries no data — its presence is the whole signal.
/// </remarks>
public sealed class SynapseEndpointMarker
{
    private SynapseEndpointMarker()
    {
    }

    /// <summary>The single marker instance attached to every Synapse-mapped endpoint.</summary>
    internal static SynapseEndpointMarker Instance { get; } = new();
}
