using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

/// <summary>Lists every task.</summary>
public sealed record ListTasksQuery : IRequest<IReadOnlyList<TaskDto>>;

/// <summary>Gets one task by its id.</summary>
/// <remarks>
///     <see cref="TaskId" /> is <c>required</c>, which a route-bound property could not be until the
///     binder started setting such properties in the object initializer of its <c>new</c> expression:
///     it used to construct the message and assign afterwards, and no assignment satisfies
///     <c>required</c>. Left as a live example of the shape, since a route parameter is always
///     present by the time the binder runs.
/// </remarks>
public sealed record GetTaskQuery : IRequest<TaskDto>
{
    /// <summary>The task's id, bound from the route.</summary>
    public required Guid TaskId { get; init; }
}

/// <summary>Streams every task, one at a time.</summary>
public sealed record StreamTasksQuery : IStreamRequest<TaskDto>;

/// <summary>Creates a new task.</summary>
public sealed record CreateTaskCommand : IRequest<TaskCreated>
{
    /// <summary>The task's title.</summary>
    public required string Title { get; init; }
}

/// <summary>The id of the task a <see cref="CreateTaskCommand" /> just created.</summary>
public sealed record TaskCreated
{
    /// <summary>The new task's id.</summary>
    public required Guid TaskId { get; init; }
}

/// <summary>Updates a task's title.</summary>
/// <remarks>
///     <see cref="TaskId" /> is bound from the route, not the request body, and is deliberately not
///     <c>required</c> — unlike <see cref="GetTaskQuery.TaskId" />, which is. The difference is the
///     body: this message is deserialized from JSON first (populating <see cref="Title" />) and the
///     route value applied to the result afterwards, so marking it <c>required</c> would make the
///     source-generated deserializer demand it in the payload the client sends, which is the one
///     place it is not.
/// </remarks>
public sealed record UpdateTaskCommand : IRequest
{
    /// <summary>The task's id, bound from the route.</summary>
    public Guid TaskId { get; init; }

    /// <summary>The task's new title.</summary>
    public required string Title { get; init; }
}

/// <summary>Deletes a task.</summary>
/// <remarks>
///     A bodyless verb binding only from the route, so <see cref="TaskId" /> can be <c>required</c>
///     for the same reason <see cref="GetTaskQuery.TaskId" /> is.
/// </remarks>
public sealed record DeleteTaskCommand : IRequest
{
    /// <summary>The task's id, bound from the route.</summary>
    public required Guid TaskId { get; init; }
}

/// <summary>A task, as returned to clients.</summary>
public sealed record TaskDto
{
    /// <summary>The task's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The task's title.</summary>
    public required string Title { get; init; }
}

/// <summary>Searches tasks whose title contains <see cref="Title" />.</summary>
/// <remarks>
///     <see cref="Title" /> carries an explicit <c>[FromQuery]</c> rather than relying on convention.
///     <see cref="SearchTasksEndpoint" /> declares its route in <c>Configure</c>, so the generator has
///     no verb string to resolve binding sources from and assumes a bodyless verb; annotating the
///     property explicitly is what keeps SYNE014 silent, and is the advice SYNE014 itself gives.
///     It is nullable, so an absent <c>?title=</c> is optional rather than a bind failure.
/// </remarks>
public sealed record SearchTasksQuery : IRequest<IReadOnlyList<TaskDto>>
{
    /// <summary>The title fragment to match, bound from the query string.</summary>
    [FromQuery]
    public string? Title { get; init; }
}
