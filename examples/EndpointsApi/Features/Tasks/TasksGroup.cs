using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

/// <summary>Shared prefix and tag for every task endpoint.</summary>
/// <remarks>
///     The base class is referenced by its fully-qualified name. The endpoints generator emits a
///     sealed <c>EndpointGroup</c> class directly into this assembly's root namespace
///     (<c>UnambitiousFx.Examples.EndpointsApi</c>), which is an ancestor of this file's namespace —
///     so an unqualified <c>EndpointGroup</c> here resolves to that generated type instead of
///     <see cref="global::UnambitiousFx.Synapse.Endpoints.EndpointGroup" />, and derivation fails
///     with CS0509 (cannot derive from sealed type). Qualifying the name (or importing it under an
///     alias) avoids the collision.
/// </remarks>
public sealed class TasksGroup : global::UnambitiousFx.Synapse.Endpoints.EndpointGroup
{
    /// <inheritdoc />
    public override void Configure(IEndpointGroupBuilder builder)
    {
        builder.Prefix("/tasks").Tag("Tasks");
    }
}
