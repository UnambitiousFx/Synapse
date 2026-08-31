using UnambitiousFx.Synapse.Endpoints;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

/// <summary>Shared prefix and tag for every task endpoint.</summary>
public sealed class TasksGroup : EndpointGroup
{
    /// <inheritdoc />
    public override void Configure(IEndpointGroupBuilder builder)
    {
        builder.Prefix("/tasks").Tag("Tasks");
    }
}
