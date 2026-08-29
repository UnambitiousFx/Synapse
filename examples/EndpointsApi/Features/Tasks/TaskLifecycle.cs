using Microsoft.AspNetCore.Http;
using UnambitiousFx.Examples.EndpointsApi.Infrastructure;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

// The three response mappers that Endpoints.cs does not reach for. CreateTaskEndpoint already shows
// Created(); these show the rest of IEndpointBuilder<TResponse>'s success shaping, including the two
// calls whose point is that the handler's value is deliberately NOT what goes on the wire.

/// <summary>Queues a task for archival.</summary>
/// <remarks>
///     <see cref="TaskId" /> is not <c>required</c>, for the same reason
///     <see cref="UpdateTaskCommand.TaskId" /> is not: <c>POST</c> carries a body, so the message is
///     JSON-deserialized before the route value is applied, and <c>required</c> would make the
///     source-generated deserializer demand a <c>taskId</c> in a payload that never carries one.
/// </remarks>
public sealed record ArchiveTaskCommand : IRequest<TaskArchived>
{
    /// <summary>The task's id, bound from the route.</summary>
    public Guid TaskId { get; init; }
}

/// <summary>Acknowledges an archival request.</summary>
/// <param name="TaskId">The task that was queued.</param>
public sealed record TaskArchived(Guid TaskId);

/// <summary>
///     Responds <c>202 Accepted</c> with a <c>Location</c> pointing at the resource the caller should
///     poll — the mapper for work that has been taken on but not finished.
/// </summary>
/// <remarks>
///     Worth knowing: <c>POST</c> is not a bodyless verb, so the generated binder reads a JSON body
///     even though every property here binds from the route. A caller must send <c>{}</c>. Sending
///     nothing answers <c>400</c> either way, with whichever check fails first: an empty body under
///     <c>Content-Type: application/json</c> gives "The request body is required but was empty or
///     null.", while a request with no content type at all gives "The request body is required to be
///     JSON, but the request declared content type ''." Both verified against the running app.
/// </remarks>
[Post("/{taskId:guid}/archive")]
[InGroup<TasksGroup>]
public sealed class ArchiveTaskEndpoint : Endpoint<ArchiveTaskCommand, TaskArchived>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder<TaskArchived> builder)
    {
        builder.Accepted(archived => $"/tasks/{archived.TaskId}")
            .Summary("Queue a task for archival");
    }
}

/// <summary>Renames a task.</summary>
public sealed record RetitleTaskCommand : IRequest<TaskDto>
{
    /// <summary>The task's id, bound from the route.</summary>
    public Guid TaskId { get; init; }

    /// <summary>The task's new title, bound from the body.</summary>
    public required string Title { get; init; }
}

/// <summary>
///     Responds <c>204 No Content</c> from an endpoint whose handler does return a value.
/// </summary>
/// <remarks>
///     The distinction that makes <c>NoContent()</c> worth having on
///     <see cref="IEndpointBuilder{TResponse}" /> at all: the handler produces a <see cref="TaskDto" />,
///     and this route deliberately does not publish it. Declaring it here rather than changing the
///     handler keeps the wire contract a property of the endpoint.
/// </remarks>
[Put("/{taskId:guid}/title")]
[InGroup<TasksGroup>]
public sealed class RetitleTaskEndpoint : Endpoint<RetitleTaskCommand, TaskDto>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder<TaskDto> builder)
    {
        builder.NoContent()
            .Summary("Rename a task, publishing nothing back");
    }
}

/// <summary>Asks the store to compact itself.</summary>
public sealed record CompactTasksCommand : IRequest<CompactReport>;

/// <summary>What a compaction did.</summary>
/// <param name="Examined">How many tasks were looked at.</param>
public sealed record CompactReport(int Examined);

/// <summary>
///     Responds with a status code the builder has no named method for.
/// </summary>
/// <remarks>
///     <c>StatusCode(int)</c> is the general form of <c>NoContent()</c> — a fixed success code and no
///     body. <c>205 Reset Content</c> is the honest use for it here: it tells the caller its view of
///     the collection is stale, which is exactly what a compaction means and which no named mapper
///     covers. Like <see cref="ArchiveTaskEndpoint" />, this is a <c>POST</c> and so still expects
///     <c>{}</c> on the wire.
/// </remarks>
[Post("/compact")]
[InGroup<TasksGroup>]
public sealed class CompactTasksEndpoint : Endpoint<CompactTasksCommand, CompactReport>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder<CompactReport> builder)
    {
        builder.StatusCode(StatusCodes.Status205ResetContent)
            .Summary("Compact the store");
    }
}

/// <summary>Handles <see cref="ArchiveTaskCommand" />.</summary>
[RequestHandler<ArchiveTaskCommand, TaskArchived>]
public sealed class ArchiveTaskCommandHandler : IRequestHandler<ArchiveTaskCommand, TaskArchived>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="ArchiveTaskCommandHandler" /> class.</summary>
    /// <param name="repository">The task store.</param>
    public ArchiveTaskCommandHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public ValueTask<Result<TaskArchived>> HandleAsync(ArchiveTaskCommand request,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_repository.GetById(request.TaskId) is not null
            ? Result.Success(new TaskArchived(request.TaskId))
            : Result.FailNotFound<TaskArchived>("Task", request.TaskId.ToString()));
    }
}

/// <summary>Handles <see cref="RetitleTaskCommand" />.</summary>
[RequestHandler<RetitleTaskCommand, TaskDto>]
public sealed class RetitleTaskCommandHandler : IRequestHandler<RetitleTaskCommand, TaskDto>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="RetitleTaskCommandHandler" /> class.</summary>
    /// <param name="repository">The task store.</param>
    public RetitleTaskCommandHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public ValueTask<Result<TaskDto>> HandleAsync(RetitleTaskCommand request,
        CancellationToken cancellationToken = default)
    {
        if (!_repository.Update(request.TaskId, request.Title))
        {
            return ValueTask.FromResult(Result.FailNotFound<TaskDto>("Task", request.TaskId.ToString()));
        }

        return ValueTask.FromResult(Result.Success(_repository.GetById(request.TaskId)!));
    }
}

/// <summary>Handles <see cref="CompactTasksCommand" />.</summary>
[RequestHandler<CompactTasksCommand, CompactReport>]
public sealed class CompactTasksCommandHandler : IRequestHandler<CompactTasksCommand, CompactReport>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="CompactTasksCommandHandler" /> class.</summary>
    /// <param name="repository">The task store.</param>
    public CompactTasksCommandHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public ValueTask<Result<CompactReport>> HandleAsync(CompactTasksCommand request,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Result.Success(new CompactReport(_repository.GetAll().Count)));
    }
}
