using UnambitiousFx.Examples.EndpointsApi.Infrastructure;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints;
using System.Runtime.CompilerServices;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

/// <summary>Streams the tasks whose title contains <see cref="Title" />.</summary>
/// <remarks>
///     Unlike <see cref="StreamTasksQuery" />, this one carries input, and it arrives in the body:
///     <c>POST</c> is not a bodyless verb, so rule 5 sends <see cref="Title" /> to the JSON body rather
///     than to the query string.
/// </remarks>
public sealed record StreamSearchTasksQuery : IStreamRequest<TaskDto>
{
    /// <summary>The title fragment to match, bound from the request body.</summary>
    public required string Title { get; init; }
}

/// <summary>
///     A stream whose request has a body, declared through <c>IStreamEndpointBuilder.Post</c>'s
///     attribute equivalent.
/// </summary>
/// <remarks>
///     The transport is negotiated exactly as it is for the bodyless <c>GET /tasks/stream</c> — an
///     <c>Accept</c> of <c>text/event-stream</c> yields server-sent events, anything else yields a JSON
///     array written as items arrive. What differs is only where the request came from.
/// </remarks>
[Post("/stream/search")]
[InGroup<TasksGroup>]
public sealed class StreamSearchTasksEndpoint : StreamEndpoint<StreamSearchTasksQuery, TaskDto>;

/// <summary>Handles <see cref="StreamSearchTasksQuery" />.</summary>
/// <remarks>
///     Deliberately yields a failure for a task with a blank title, to pin what a failed item does to
///     a stream that has already begun: <c>IHttpInvoker.InvokeStreamAsync</c> skips it and the
///     remaining items keep arriving. The status line was committed before the first item, so a
///     mid-stream failure cannot change it — this is why a stream reports per-item problems by
///     skipping rather than by status code.
/// </remarks>
[StreamRequestHandler<StreamSearchTasksQuery, TaskDto>]
public sealed class StreamSearchTasksQueryHandler
    : IStreamRequestHandler<StreamSearchTasksQuery, TaskDto>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="StreamSearchTasksQueryHandler" /> class.</summary>
    /// <param name="repository">The task store.</param>
    public StreamSearchTasksQueryHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Result<TaskDto>> HandleAsync(
        StreamSearchTasksQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var task in _repository.GetAll())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(task.Title))
            {
                yield return Result.Failure<TaskDto>($"Task {task.Id} has no title.");
                continue;
            }

            if (task.Title.Contains(request.Title, StringComparison.OrdinalIgnoreCase))
            {
                yield return Result.Success(task);
            }

            await Task.Yield();
        }
    }
}
