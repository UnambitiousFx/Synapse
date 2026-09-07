using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

/// <summary>Lists every task. No configuration needed.</summary>
[Get("/")]
[InGroup<TasksGroup>]
public sealed partial class ListTasksEndpoint : Endpoint<ListTasksQuery, IReadOnlyList<TaskDto>>;

/// <summary>Gets one task. TaskId binds from the route by name.</summary>
/// <remarks>
///     The <c>404</c> is the part nothing can infer. Two outcomes are declared for you — the success
///     status, and the <c>400</c> a binding failure sends — while every status the registered
///     <c>IFailureHttpMapper</c> writes for a failed <c>Result</c> is invisible to the document until
///     the endpoint names it. <c>GetTaskQueryHandler</c> answers an unknown id with
///     <c>Result.FailNotFound(...)</c>, so without this call the published contract would claim
///     <c>200</c> and <c>400</c> are the only things this route can return.
/// </remarks>
[Get("/{taskId:guid}")]
[InGroup<TasksGroup>]
public sealed partial class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder<TaskDto> builder)
    {
        builder.ProducesProblem(StatusCodes.Status404NotFound);
    }
}

/// <summary>Streams tasks; the transport is negotiated on Accept.</summary>
[Get("/stream")]
[InGroup<TasksGroup>]
public sealed partial class StreamTasksEndpoint : StreamEndpoint<StreamTasksQuery, TaskDto>;

/// <summary>Creates a task, responding 201 with a Location header.</summary>
[Post("/")]
[InGroup<TasksGroup>]
public sealed partial class CreateTaskEndpoint : Endpoint<CreateTaskCommand, TaskCreated>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder<TaskCreated> builder)
    {
        builder.Created(created => $"/tasks/{created.TaskId}")
            .Summary("Create a task");
    }
}

/// <summary>Updates a task. Responds 204, or 404 when the id is unknown.</summary>
/// <remarks>
///     The arity with no response declares its failures through the non-generic
///     <see cref="IEndpointBuilder" />, which is the only difference from
///     <see cref="GetTaskEndpoint" />.
/// </remarks>
[Put("/{taskId:guid}")]
[InGroup<TasksGroup>]
public sealed partial class UpdateTaskEndpoint : Endpoint<UpdateTaskCommand>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder builder)
    {
        builder.ProducesProblem(StatusCodes.Status404NotFound);
    }
}

/// <summary>Deletes a task. Responds 204, or 404 when the id is unknown.</summary>
[Delete("/{taskId:guid}")]
[InGroup<TasksGroup>]
public sealed partial class DeleteTaskEndpoint : Endpoint<DeleteTaskCommand>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder builder)
    {
        builder.ProducesProblem(StatusCodes.Status404NotFound);
    }
}

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
public sealed partial class SearchTasksEndpoint : Endpoint<SearchTasksQuery, IReadOnlyList<TaskDto>>
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
