using UnambitiousFx.Synapse.Endpoints;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

/// <summary>Lists every task. No configuration needed.</summary>
[Get("/")]
[InGroup<TasksGroup>]
public sealed class ListTasksEndpoint : Endpoint<ListTasksQuery, IReadOnlyList<TaskDto>>;

/// <summary>Gets one task. TaskId binds from the route by name.</summary>
[Get("/{taskId:guid}")]
[InGroup<TasksGroup>]
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>;

/// <summary>Streams tasks; the transport is negotiated on Accept.</summary>
[Get("/stream")]
[InGroup<TasksGroup>]
public sealed class StreamTasksEndpoint : StreamEndpoint<StreamTasksQuery, TaskDto>;

/// <summary>Creates a task, responding 201 with a Location header.</summary>
[Post("/")]
[InGroup<TasksGroup>]
public sealed class CreateTaskEndpoint : Endpoint<CreateTaskCommand, TaskCreated>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder<TaskCreated> builder)
    {
        builder.Created(created => $"/tasks/{created.TaskId}")
            .Summary("Create a task");
    }
}

/// <summary>Updates a task. Responds 204.</summary>
[Put("/{taskId:guid}")]
[InGroup<TasksGroup>]
public sealed class UpdateTaskEndpoint : Endpoint<UpdateTaskCommand>;

/// <summary>Deletes a task. Responds 204.</summary>
[Delete("/{taskId:guid}")]
[InGroup<TasksGroup>]
public sealed class DeleteTaskEndpoint : Endpoint<DeleteTaskCommand>;
