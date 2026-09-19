using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints;

/// <summary>
///     A group of endpoints sharing a route prefix, tags and authorization policies. Endpoints join
///     a group with <c>InGroupAttribute</c>.
/// </summary>
public abstract class EndpointGroup
{
    /// <summary>Configures the group. Called once at startup, before its endpoints are mapped.</summary>
    /// <param name="builder">The group builder.</param>
    public abstract void Configure(IEndpointGroupBuilder builder);
}
