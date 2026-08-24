namespace UnambitiousFx.Synapse.Endpoints.Internal;

/// <summary>
///     Endpoint metadata attached by <see cref="EndpointMapper.Map" /> to every endpoint this library
///     maps, so the startup duplicate-route check can tell Synapse's own endpoints apart from
///     everything else in the route table.
/// </summary>
/// <remarks>
///     A private nested type rather than a public marker: nothing outside this assembly has a reason
///     to look for it, and making it public would invite consumers to attach it by hand and be counted
///     by a check that has no way to reason about their endpoints. One shared
///     <see cref="Instance" /> because the marker carries no data — its presence is the whole signal.
/// </remarks>
internal sealed class SynapseEndpointMarker
{
    private SynapseEndpointMarker()
    {
    }

    /// <summary>The single marker instance attached to every Synapse-mapped endpoint.</summary>
    internal static SynapseEndpointMarker Instance { get; } = new();
}
