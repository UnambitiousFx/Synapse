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

/// <summary>
///     Searches tasks by title. Declares its route in <c>Configure</c> rather than through a route
///     attribute — the "computed route" escape hatch documented in
///     <c>docs/docs/endpoints/reference/escape-hatches.mdx</c>.
///     Kept in the example precisely because that shape used to be broken: a route declared only in
///     <c>Configure</c> left the generator with no verb string to reason about, so it emitted a
///     request-body read for what is in fact a <c>GET</c> and every request 500'd. See
///     <c>SearchTasksQuery</c> for the binding side of the same story.
/// </summary>
[InGroup<TasksGroup>]
public sealed class SearchTasksEndpoint : Endpoint<SearchTasksQuery, IReadOnlyList<TaskDto>>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder<IReadOnlyList<TaskDto>> builder)
    {
        // A route that genuinely cannot be a constant expression: this is the case the escape hatch
        // exists for, and the reason no [Get(...)] attribute can be used here.
        var segment = Environment.GetEnvironmentVariable("SEARCH_ROUTE_SEGMENT") ?? "search";
        builder.Get($"/{segment}").Summary("Search tasks by title");
    }
}
