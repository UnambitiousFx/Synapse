using UnambitiousFx.Examples.EndpointsApi.Infrastructure;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Ops;

/// <summary>Deletes every task whose title contains any of <paramref name="Tags" />.</summary>
/// <param name="Tags">The tags to purge, at least one.</param>
public sealed record PurgeTasksCommand(IReadOnlyList<string> Tags) : IRequest;

/// <summary>
///     The middle level at the arity with no response: hand-written binding, inherited dispatch, and
///     a <c>204</c> the base class supplies.
/// </summary>
/// <remarks>
///     <para>
///         The reason it cannot be an <see cref="Endpoint{TRequest}" />: the tags arrive as one
///         comma-separated header, and <c>[FromHeader]</c> binds a header to a single property value —
///         splitting one header into a collection is not something the five rules express. That is the
///         "a header that must be split" case the low-level guide names.
///     </para>
///     <para>
///         Contrast <c>TagReportEndpoint</c>, which drops a tier for the other collection reason — one
///         repeated query key. Both write only <see cref="BindAsync" />; everything after it is the
///         same code the high level runs.
///     </para>
///     <para>
///         <c>DELETE</c> is a bodyless verb, so this endpoint declares no request body at all — which
///         is why the header is the whole input.
///     </para>
/// </remarks>
[Delete("/ops/tasks")]
public sealed class PurgeTasksEndpoint : RawEndpoint<PurgeTasksCommand>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder builder)
    {
        builder.Tag("Ops")
            .Summary("Delete every task matching any of the X-Purge-Tags header's tags");
    }

    /// <inheritdoc />
    public override ValueTask<BindResult<PurgeTasksCommand>> BindAsync(HttpContext context)
    {
        var validation = context.Validate();

        var tags = (context.Header("X-Purge-Tags") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        validation.Check(tags.Length > 0, "X-Purge-Tags", "at least one tag is required");

        return ValueTask.FromResult(validation.IsValid
            ? BindResult<PurgeTasksCommand>.Success(new PurgeTasksCommand(tags))
            : BindResult<PurgeTasksCommand>.Failure(validation));
    }
}

/// <summary>Handles <see cref="PurgeTasksCommand" /> by deleting every matching task.</summary>
[RequestHandler<PurgeTasksCommand>]
public sealed class PurgeTasksCommandHandler : IRequestHandler<PurgeTasksCommand>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="PurgeTasksCommandHandler" /> class.</summary>
    /// <param name="repository">The task store.</param>
    public PurgeTasksCommandHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public ValueTask<Result> HandleAsync(PurgeTasksCommand request,
        CancellationToken cancellationToken = default)
    {
        var doomed = _repository.GetAll()
            .Where(task => request.Tags.Any(tag =>
                task.Title.Contains(tag, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        foreach (var task in doomed)
        {
            _repository.Delete(task.Id);
        }

        return ValueTask.FromResult(Result.Success());
    }
}
