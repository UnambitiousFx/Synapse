using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

/// <summary>Lists every task.</summary>
public sealed record ListTasksQuery : IRequest<IReadOnlyList<TaskDto>>;

/// <summary>Gets one task by its id.</summary>
/// <remarks>
///     <see cref="TaskId" /> is deliberately not <c>required</c>: the generated binder for a
///     route-only message constructs it with a bare <c>new GetTaskQuery()</c> and then applies the
///     bound value via a <c>with</c> expression — a call site that cannot satisfy a <c>required</c>
///     member, so marking a purely route/query/header-bound property <c>required</c> fails to
///     compile against the current generator output.
/// </remarks>
public sealed record GetTaskQuery : IRequest<TaskDto>
{
    /// <summary>The task's id, bound from the route.</summary>
    public Guid TaskId { get; init; }
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
///     <see cref="TaskId" /> is bound from the route, not the request body, so it is deliberately
///     not <c>required</c>: the generated binder deserializes the JSON body into this type first
///     (populating <see cref="Title" />) and applies <see cref="TaskId" /> afterwards via a
///     <c>with</c> expression. A <c>required</c> route-bound property would additionally require
///     the client's JSON body to include it, since the source-generated deserializer enforces
///     <c>required</c> members against the payload actually received.
/// </remarks>
public sealed record UpdateTaskCommand : IRequest
{
    /// <summary>The task's id, bound from the route.</summary>
    public Guid TaskId { get; init; }

    /// <summary>The task's new title.</summary>
    public required string Title { get; init; }
}

/// <summary>Deletes a task.</summary>
/// <remarks>See <see cref="GetTaskQuery" /> for why <see cref="TaskId" /> is not <c>required</c>.</remarks>
public sealed record DeleteTaskCommand : IRequest
{
    /// <summary>The task's id, bound from the route.</summary>
    public Guid TaskId { get; init; }
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
