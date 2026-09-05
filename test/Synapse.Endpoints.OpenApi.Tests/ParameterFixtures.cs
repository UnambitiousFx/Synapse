using Microsoft.AspNetCore.Mvc;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi.Tests;

/// <summary>A task, as these fixtures return it.</summary>
public sealed record TaskDto
{
    /// <summary>The task's id.</summary>
    public required Guid Id { get; init; }
}

/// <summary>
///     A query bound entirely from the query string: one required scalar and one optional
///     collection.
/// </summary>
public sealed record SearchTasksQuery : IRequest<IReadOnlyList<TaskDto>>
{
    /// <summary>The page to return, bound from <c>?page=</c> by the bodyless-verb convention.</summary>
    public required int Page { get; init; }

    /// <summary>The tags to filter on, repeated as <c>?tag=a&amp;tag=b</c>.</summary>
    /// <remarks>
    ///     Non-nullable on purpose, and still documented <c>required: false</c>: an absent repeated
    ///     key binds an empty collection rather than failing, so a collection-shaped parameter is
    ///     never required however it is declared.
    /// </remarks>
    [FromQuery(Name = "tag")]
    public string[] Tags { get; init; } = [];
}

/// <summary>Searches tasks. Reads only the query string.</summary>
[Get("/tasks/search")]
public sealed class SearchTasksEndpoint : Endpoint<SearchTasksQuery, IReadOnlyList<TaskDto>>;

/// <summary>A query bound from the route and a header.</summary>
public sealed record GetTaskQuery : IRequest<TaskDto>
{
    /// <summary>The task's id, matched to the <c>{taskId}</c> route parameter by name.</summary>
    public required Guid TaskId { get; init; }

    /// <summary>The calling tenant. Headers are never bound by convention, hence the attribute.</summary>
    [FromHeader("X-Tenant")]
    public required string Tenant { get; init; }
}

/// <summary>Gets one task. Reads the route and one header, and nothing from the query string.</summary>
/// <remarks>
///     The route constraint is deliberate: OpenAPI strips it, so the document path is
///     <c>/tasks/{taskId}</c> while the template is <c>/tasks/{taskId:guid}</c>.
/// </remarks>
[Get("/tasks/{taskId:guid}")]
public sealed class GetTaskEndpoint : Endpoint<GetTaskQuery, TaskDto>;

/// <summary>A query whose route-bound property is nullable, so its binder reports it optional.</summary>
/// <remarks>
///     The point of this fixture: a nullable route property binds null instead of failing, so the
///     binder's own <c>Required</c> is <see langword="false" /> — and OpenAPI 3.x still forbids an
///     optional path parameter, so the document must override it.
/// </remarks>
public sealed record PeekTaskQuery : IRequest<TaskDto>
{
    /// <summary>The task's id, matched to the <c>{taskId}</c> route parameter by name.</summary>
    public Guid? TaskId { get; init; }
}

/// <summary>Peeks at one task. The nullable-route-property case.</summary>
[Get("/tasks/{taskId:guid}/peek")]
public sealed class PeekTaskEndpoint : Endpoint<PeekTaskQuery, TaskDto>;
