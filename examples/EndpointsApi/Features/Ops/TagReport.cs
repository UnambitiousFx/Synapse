using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Examples.EndpointsApi.Infrastructure;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Ops;

/// <summary>
///     A report request whose tags arrive as a repeated query key (<c>?tag=a&amp;tag=b</c>).
/// </summary>
/// <remarks>
///     Bound by hand rather than by convention: a collection built from one repeated key is exactly
///     what the five binding conventions cannot express, which is why <c>TagReportEndpoint</c> derives
///     from <c>RawEndpoint&lt;TRequest, TResponse&gt;</c> instead of <c>Endpoint&lt;…&gt;</c>.
/// </remarks>
/// <param name="Page">The 1-based page number.</param>
/// <param name="Size">The page size.</param>
/// <param name="Tags">The tags to match, at least one.</param>
public sealed record TagReportQuery(int Page, int Size, IReadOnlyList<string> Tags)
    : IRequest<TagReportDto>;

/// <summary>The report.</summary>
/// <param name="Page">The page that was requested.</param>
/// <param name="Matched">How many tasks matched any of the tags.</param>
/// <param name="Tags">The tags that were searched.</param>
public sealed record TagReportDto(int Page, int Matched, IReadOnlyList<string> Tags);

/// <summary>Builds a <see cref="TagReportDto" />.</summary>
[RequestHandler<TagReportQuery, TagReportDto>]
public sealed class TagReportQueryHandler : IRequestHandler<TagReportQuery, TagReportDto>
{
    private readonly TaskRepository _repository;

    /// <summary>Initializes a new instance of the <see cref="TagReportQueryHandler" /> class.</summary>
    /// <param name="repository">The task store.</param>
    public TagReportQueryHandler(TaskRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public ValueTask<Result<TagReportDto>> HandleAsync(TagReportQuery request,
        CancellationToken cancellationToken = default)
    {
        var matched = _repository.GetAll()
            .Count(task => request.Tags.Any(tag =>
                task.Title.Contains(tag, StringComparison.OrdinalIgnoreCase)));

        return ValueTask.FromResult(
            Result.Success(new TagReportDto(request.Page, matched, request.Tags)));
    }
}
