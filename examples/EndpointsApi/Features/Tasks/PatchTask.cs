using System.Text.Json.Serialization;
using UnambitiousFx.Examples.EndpointsApi.Infrastructure;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Tasks;

/// <summary>Renames a task, recording who asked and when.</summary>
/// <remarks>
///     <para>
///         Every one of the five binding rules is visible on this one message, which is why it lives in
///         its own file rather than in <c>Messages.cs</c>: that file imports
///         <c>Microsoft.AspNetCore.Mvc</c> for its <c>[FromQuery]</c>, and importing both namespaces
///         would make the bare name <c>[FromHeader]</c> ambiguous — the attribute collision the
///         escape-hatches page warns about. Here only the Synapse namespace is in scope.
///     </para>
///     <para>
///         Rule 3 binds <see cref="TaskId" /> from the route, rule 5 binds <see cref="Title" /> from the
///         body, rule 2 binds <see cref="Actor" /> from a header (never bound by convention), and rule 1
///         excludes <see cref="StampedAt" /> from the generated bindings so that
///         <c>StampPatchBehavior</c> owns it — see that property's remarks for why rule 1 needs
///         <c>[JsonIgnore]</c> next to it on a verb that carries a body.
///     </para>
/// </remarks>
public sealed record PatchTaskCommand : IRequest<TaskPatched>
{
    /// <summary>Rule 3 — the name matches <c>{taskId}</c> in the route template.</summary>
    public Guid TaskId { get; init; }

    /// <summary>Rule 5 — <c>PATCH</c> carries a body and nothing else claimed this property.</summary>
    public required string Title { get; init; }

    /// <summary>Rule 2 — headers are never bound by convention, so the attribute is the only way.</summary>
    [FromHeader("X-Actor")]
    public string? Actor { get; init; }

    /// <summary>Rule 1 — set by the pipeline, never by the caller.</summary>
    /// <remarks>
    ///     <c>[JsonIgnore]</c> is not redundant here, and leaving it off is the trap this property
    ///     exists to show. <c>[NotBound]</c> excludes a property from the bindings the analyzer
    ///     generates — the route, query and header assignments — but a body-carrying verb is bound by
    ///     deserializing the whole message from JSON in one shot, and
    ///     <c>System.Text.Json</c> knows nothing about <c>[NotBound]</c>. Without <c>[JsonIgnore]</c>,
    ///     a caller who sends <c>{"stampedAt":"2000-01-01T00:00:00Z"}</c> populates this property, and
    ///     only <c>StampPatchBehavior</c> overwriting it unconditionally keeps that from reaching the
    ///     handler. Verified against the running app: with the behaviour's rewrite removed and no
    ///     <c>[JsonIgnore]</c>, the forged value arrives intact; with <c>[JsonIgnore]</c>, it does not.
    ///     On a bodyless verb <c>[NotBound]</c> alone is sufficient, because nothing deserializes the
    ///     message.
    /// </remarks>
    [NotBound]
    [JsonIgnore]
    public DateTimeOffset StampedAt { get; init; }
}

/// <summary>What a patch reports back, so the bound header and the stamp are observable.</summary>
/// <param name="TaskId">The patched task's id.</param>
/// <param name="Actor">Whoever the <c>X-Actor</c> header named, or <see langword="null" />.</param>
/// <param name="StampedAt">When the pipeline stamped the command.</param>
public sealed record TaskPatched(Guid TaskId, string? Actor, DateTimeOffset StampedAt);

/// <summary>Renames a task through <c>PATCH</c>, the verb the other examples do not use.</summary>
[Patch("/{taskId:guid}")]
[InGroup<TasksGroup>]
public sealed class PatchTaskEndpoint : Endpoint<PatchTaskCommand, TaskPatched>;

/// <summary>
///     Stamps <see cref="PatchTaskCommand.StampedAt" /> on its way to the handler.
/// </summary>
/// <remarks>
///     This is what a <c>[NotBound]</c> property is for, and why the pipeline rather than the handler
///     sets it: <c>next</c> takes the request, so a behaviour can hand the tier below a rewritten
///     message. The handler then reads a value it never had to compute.
///
///     The rewrite is unconditional on purpose. Stamping only when the property is still at its
///     default would hand a caller control of it on any verb where the message is deserialized from
///     JSON — see <see cref="PatchTaskCommand.StampedAt" />.
/// </remarks>
[PipelineBehavior]
public sealed class StampPatchBehavior : IRequestPipelineBehavior<PatchTaskCommand, TaskPatched>
{
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="StampPatchBehavior" /> class.</summary>
    /// <param name="timeProvider">The clock, injected so a test can pin the stamp.</param>
    public StampPatchBehavior(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public ValueTask<Result<TaskPatched>> HandleAsync(PatchTaskCommand request,
        RequestHandlerDelegate<PatchTaskCommand, TaskPatched> next,
        CancellationToken cancellationToken = default)
    {
        return next(request with { StampedAt = _timeProvider.GetUtcNow() }, cancellationToken);
    }
}

/// <summary>Handles <see cref="PatchTaskCommand" /> by renaming the task.</summary>
[RequestHandler<PatchTaskCommand, TaskPatched>]
public sealed class PatchTaskCommandHandler : IRequestHandler<PatchTaskCommand, TaskPatched>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="PatchTaskCommandHandler" /> class.</summary>
    /// <param name="repository">The task store.</param>
    public PatchTaskCommandHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public ValueTask<Result<TaskPatched>> HandleAsync(PatchTaskCommand request,
        CancellationToken cancellationToken = default)
    {
        var updated = _repository.Update(request.TaskId, request.Title);

        return ValueTask.FromResult(updated
            ? Result.Success(new TaskPatched(request.TaskId, request.Actor, request.StampedAt))
            : Result.FailNotFound<TaskPatched>("Task", request.TaskId.ToString()));
    }
}
