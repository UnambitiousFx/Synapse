using Microsoft.AspNetCore.Http;
using UnambitiousFx.Examples.EndpointsApi.Infrastructure;
using UnambitiousFx.Synapse.Endpoints;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Examples.EndpointsApi.Features.Ops;

/// <summary>The health payload. Not a CQRS message — it never goes near the mediator.</summary>
/// <param name="Status">Always <c>"ok"</c> when the process is serving.</param>
/// <param name="Tasks">How many tasks are stored.</param>
public sealed record HealthDto(string Status, int Tasks);

/// <summary>
///     The free-form low level: no message, no dispatch, no binder. The whole endpoint is one method
///     that reads the context and decides.
/// </summary>
/// <remarks>
///     This is the shape the high level cannot express, and the reason the low level exists. It
///     answers <c>304 Not Modified</c> from an <c>If-None-Match</c> header, which is a decision about
///     the HTTP exchange itself rather than about a command or a query — there is no message to
///     dispatch and no response DTO to map, so <see cref="Endpoint{TRequest,TResponse}" /> has nothing
///     to work with.
/// </remarks>
[Get("/health")]
public sealed class HealthEndpoint : RawEndpoint
{
    /// <inheritdoc />
    /// <remarks>
    ///     Nothing about a hand-written handler can be inferred, so what it produces is declared here
    ///     explicitly. Declaring nothing would also be valid, and would emit nothing.
    /// </remarks>
    public override void Configure(IRawEndpointBuilder builder)
    {
        builder.Produces<HealthDto>()
            .Produces(StatusCodes.Status304NotModified)
            .Tag("Ops")
            .Summary("Liveness, with an ETag");
    }

    /// <inheritdoc />
    public override ValueTask<IResult> HandleAsync(HttpContext context,
        CancellationToken cancellationToken)
    {
        // No constructor injection: endpoints are startup singletons, so dependencies come off the
        // request. context.Service<T>() is the low-level shorthand for that.
        var count = context.Service<TaskRepository>().GetAll().Count;
        var etag = $"\"tasks-{count}\"";

        if (context.Header("If-None-Match") == etag)
        {
            return ValueTask.FromResult(TypedResults.StatusCode(StatusCodes.Status304NotModified) as IResult);
        }

        context.Response.Headers.ETag = etag;
        return ValueTask.FromResult(TypedResults.Ok(new HealthDto("ok", count)) as IResult);
    }
}

/// <summary>
///     The mediator-bound middle level: binding is hand-written, dispatch and response mapping are
///     inherited from exactly the same code the high level runs.
/// </summary>
/// <remarks>
///     <para>
///         The reason this endpoint cannot be a high-level one: its message wants a
///         <c>IReadOnlyList&lt;string&gt;</c> of tags, and the five binding conventions have no way to
///         express "one repeated query key becomes a collection". Only the binding differs — everything
///         after <see cref="BindAsync" /> is <see cref="RawEndpoint{TRequest,TResponse}" />'s, which is
///         also <see cref="Endpoint{TRequest,TResponse}" />'s.
///     </para>
///     <para>
///         Note the single <c>400</c> listing every bad input: <c>context.Validate()</c> collects them
///         all rather than stopping at the first, and the generated binders of the high level now do
///         the same thing through the same collector.
///     </para>
/// </remarks>
[Get("/reports")]
public sealed class TagReportEndpoint : RawEndpoint<TagReportQuery, TagReportDto>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder<TagReportDto> builder)
    {
        builder.Tag("Ops")
            .Summary("Report on tasks matching any of the given tags");
    }

    /// <inheritdoc />
    public override ValueTask<BindResult<TagReportQuery>> BindAsync(HttpContext context)
    {
        var validation = context.Validate();

        // Every value is read before anything is rejected, so a caller who got two of them wrong
        // learns about both from one response.
        validation.QueryOptional<int>("size", out var size);

        // The range check is guarded on the read that feeds it. Unguarded, a request that sent no
        // page at all was told both "The query value is required." and "must be at least 1" — the
        // second derived from default(int) rather than from anything the caller actually sent.
        if (validation.Query<int>("page", out var page))
        {
            validation.Check(page >= 1, "page", "must be at least 1");
        }

        var tags = context.QueryValues("tag")
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag!)
            .ToArray();

        validation.Check(tags.Length > 0, "tag", "at least one tag is required");

        return ValueTask.FromResult(validation.IsValid
            ? BindResult<TagReportQuery>.Success(new TagReportQuery(page, size ?? 20, tags))
            : BindResult<TagReportQuery>.Failure(validation));
    }
}
