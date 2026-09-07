using System.Text;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace UnambitiousFx.Benchmarks.SynapseBenchmark;

/// <summary>
///     Compares dispatching one request through a hand-written Minimal API lambda against the
///     equivalent Synapse endpoint, so the adapter's overhead stays visible.
/// </summary>
/// <remarks>
///     Every arm's host is built through the same <see cref="BuildHost" /> service configuration —
///     same <see cref="GetThingQueryHandler" />, same <c>ConfigureHttpJsonOptions</c> call with the
///     same source-generated <see cref="BenchmarkJsonSerializerContext" />, same
///     <c>AddSynapseAspNetCore</c> / <c>AddSynapse</c> wiring — so the arms differ only in how the
///     route is mapped, except <see cref="SynapseEndpointWithProcessors" />'s host, which
///     additionally registers its two processors. In particular, the only thing that differs
///     between <see cref="HandWrittenLambda" /> and <see cref="SynapseEndpoint" /> is how the route
///     is mapped: a hand-rolled <c>MapGet</c> lambda with typed parameters versus
///     <c>MapEndpoint&lt;GetThingEndpoint&gt;()</c>, which goes through the generated route binder
///     and <c>Endpoint&lt;TRequest,TResponse&gt;</c>'s descriptor. Both call sides use the identical
///     three-argument
///     <c>IHttpInvoker.InvokeAsync(request, response =&gt; TypedResults.Ok(response), cancellationToken)</c>
///     overload with an equivalent success mapper, matching what <c>Endpoint&lt;TRequest,TResponse&gt;</c>'s
///     default <c>OnSuccess</c> does, so no part of that delta is attributable to calling a different
///     <see cref="IHttpInvoker" /> member.
/// </remarks>
[MemoryDiagnoser]
public class EndpointDispatchBenchmark
{
    private static readonly Guid ThingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly string RequestPath = $"/things/{ThingId}";
    private const string RequestBody = """{"name":"Widget"}""";

    private IHost _handWrittenHost = null!;
    private IHost _endpointHost = null!;
    private IHost _rawHost = null!;
    private IHost _hookedHost = null!;
    private IHost _processedHost = null!;
    private IHost _selfHandledHost = null!;
    private IHost _scalarQueryHost = null!;
    private IHost _collectionHost = null!;
    private HttpClient _handWritten = null!;
    private HttpClient _endpoint = null!;
    private HttpClient _raw = null!;
    private HttpClient _hooked = null!;
    private HttpClient _processed = null!;
    private HttpClient _selfHandled = null!;
    private HttpClient _scalarQuery = null!;
    private HttpClient _collection = null!;

    [GlobalSetup]
    public void Setup()
    {
        _handWrittenHost = BuildHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/things/{id:guid}",
                    (Guid id, IHttpInvoker invoker, CancellationToken ct) =>
                        invoker.InvokeAsync(new GetThingQuery { Id = id },
                            response => TypedResults.Ok(response),
                            ct));
                endpoints.MapPost("/things",
                    (CreateThingCommand command, IHttpInvoker invoker, CancellationToken ct) =>
                        invoker.InvokeAsync(command,
                            response => TypedResults.Ok(response),
                            ct));
            });
        });

        _endpointHost = BuildHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapEndpoint<GetThingEndpoint>();
                endpoints.MapEndpoint<CreateThingEndpoint>();
            });
        });

        _rawHost = BuildHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => { endpoints.MapEndpoint<RawGetThingEndpoint>(); });
        });

        _hookedHost = BuildHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => { endpoints.MapEndpoint<HookedThingEndpoint>(); });
        });

        _processedHost = BuildHost(
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => { endpoints.MapEndpoint<ProcessedThingEndpoint>(); });
            },
            services =>
            {
                services.AddScoped<BenchmarkPreProcessor>();
                services.AddScoped<BenchmarkPostProcessor>();
            });

        _selfHandledHost = BuildHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => { endpoints.MapEndpoint<SelfHandledThingEndpoint>(); });
        });

        _scalarQueryHost = BuildHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => { endpoints.MapEndpoint<SearchThingEndpoint>(); });
        });

        _collectionHost = BuildHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => { endpoints.MapEndpoint<SearchThingsEndpoint>(); });
        });

        _handWritten = _handWrittenHost.GetTestServer().CreateClient();
        _endpoint = _endpointHost.GetTestServer().CreateClient();
        _raw = _rawHost.GetTestServer().CreateClient();
        _hooked = _hookedHost.GetTestServer().CreateClient();
        _processed = _processedHost.GetTestServer().CreateClient();
        _selfHandled = _selfHandledHost.GetTestServer().CreateClient();
        _scalarQuery = _scalarQueryHost.GetTestServer().CreateClient();
        _collection = _collectionHost.GetTestServer().CreateClient();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _handWritten.Dispose();
        _endpoint.Dispose();
        _raw.Dispose();
        _hooked.Dispose();
        _processed.Dispose();
        _selfHandled.Dispose();
        _scalarQuery.Dispose();
        _collection.Dispose();
        _handWrittenHost.Dispose();
        _endpointHost.Dispose();
        _rawHost.Dispose();
        _hookedHost.Dispose();
        _processedHost.Dispose();
        _selfHandledHost.Dispose();
        _scalarQueryHost.Dispose();
        _collectionHost.Dispose();
    }

    [Benchmark(Baseline = true)]
    public Task<HttpResponseMessage> HandWrittenLambda()
    {
        return _handWritten.GetAsync(RequestPath);
    }

    [Benchmark]
    public Task<HttpResponseMessage> SynapseEndpoint()
    {
        return _endpoint.GetAsync(RequestPath);
    }

    /// <summary>
    ///     The same dispatch through the low level, where the binding is hand-written instead of
    ///     generated. Isolates what the generated binder costs: this arm and
    ///     <see cref="SynapseEndpoint" /> share every line downstream of <c>BindAsync</c>, because the
    ///     high-level class is the low-level one with its binder supplied.
    /// </summary>
    [Benchmark]
    public Task<HttpResponseMessage> RawEndpointHandWrittenBinding()
    {
        return _raw.GetAsync(RequestPath);
    }

    /// <summary>
    ///     The same dispatch with both lifecycle hooks overridden. The delta against
    ///     <see cref="SynapseEndpoint" /> is what the seam costs an endpoint that uses it.
    /// </summary>
    [Benchmark]
    public Task<HttpResponseMessage> SynapseEndpointWithHooks()
    {
        return _hooked.GetAsync(RequestPath);
    }

    /// <summary>
    ///     The same dispatch with one pre- and one post-processor registered, each resolved from the
    ///     request's services.
    /// </summary>
    [Benchmark]
    public Task<HttpResponseMessage> SynapseEndpointWithProcessors()
    {
        return _processed.GetAsync(RequestPath);
    }

    /// <summary>
    ///     The same request answered by the endpoint itself rather than dispatched. The delta against
    ///     <see cref="SynapseEndpoint" /> is what the mediator round-trip costs, isolated: both arms
    ///     bind <see cref="GetThingQuery" /> through the same generated binder and produce the same
    ///     <see cref="ThingDto" />, so everything up to and including binding is identical.
    /// </summary>
    [Benchmark]
    public Task<HttpResponseMessage> SynapseSelfHandledEndpoint()
    {
        return _selfHandled.GetAsync(RequestPath);
    }

    /// <summary>
    ///     Dispatches a request bound from a single query value — no accumulation. <see cref="SynapseEndpoint" />
    ///     binds from the route and carries no query string at all, so it cannot isolate what parsing
    ///     the query string itself costs (splitting <c>tag=a</c>, populating <c>StringValues</c>) from
    ///     what accumulating repeated values costs. This arm holds the query-parsing cost fixed so
    ///     <see cref="SynapseEndpointWithCollectionQuery" /> can be compared against it instead of
    ///     against <see cref="SynapseEndpoint" />.
    /// </summary>
    [Benchmark]
    public Task<HttpResponseMessage> SynapseEndpointWithScalarQuery()
    {
        return _scalarQuery.GetAsync("/things?tag=a");
    }

    /// <summary>
    ///     Dispatches a request bound entirely from a repeated query key. The generated binder for
    ///     this shape accumulates into a <c>List&lt;T&gt;</c> and calls <c>.ToArray()</c>. The delta
    ///     against <see cref="SynapseEndpointWithScalarQuery" /> — not against <see cref="SynapseEndpoint" />,
    ///     which differs in two dimensions at once (no query string at all, plus no accumulation) — is
    ///     what that loop and its allocations cost on their own.
    /// </summary>
    [Benchmark]
    public Task<HttpResponseMessage> SynapseEndpointWithCollectionQuery()
    {
        return _collection.GetAsync("/things?tag=a&tag=b&tag=c");
    }

    /// <summary>
    ///     The body-reading counterpart of <see cref="HandWrittenLambda" />. The GET pair above never
    ///     touches <c>BindingHelpers.ReadJsonBodyAsync</c> — a bodyless verb binds entirely from the
    ///     route — so it cannot measure anything about how the body's <c>JsonTypeInfo</c> is resolved.
    ///     These two arms are what make that path visible.
    /// </summary>
    [Benchmark]
    public Task<HttpResponseMessage> HandWrittenLambdaWithJsonBody()
    {
        return _handWritten.PostAsync("/things", NewBody());
    }

    /// <summary>Reads a JSON request body through the generated binder's <c>ReadJsonBodyAsync</c> call.</summary>
    [Benchmark]
    public Task<HttpResponseMessage> SynapseEndpointWithJsonBody()
    {
        return _endpoint.PostAsync("/things", NewBody());
    }

    /// <summary>
    ///     A fresh content instance per iteration: <see cref="HttpContent" /> is single-use, so a
    ///     shared one would fail on the second request rather than measure it.
    /// </summary>
    private static StringContent NewBody()
    {
        return new StringContent(RequestBody, Encoding.UTF8, "application/json");
    }

    /// <summary>
    ///     Builds and starts one in-memory host. Every benchmarked host calls this same method, so
    ///     the DI wiring — JSON options, <c>AddSynapseAspNetCore</c>, <c>AddSynapse</c>, the
    ///     registered handler — is not just equivalent between them, it is the identical code path.
    ///     The <paramref name="configureApp" /> delegate (how the route is mapped) differs for every
    ///     arm; <paramref name="configureServices" /> differs only for the processor arm, which
    ///     additionally registers its two processors.
    /// </summary>
    private static IHost BuildHost(Action<IApplicationBuilder> configureApp,
        Action<IServiceCollection>? configureServices = null)
    {
        return new HostBuilder()
            .ConfigureWebHost(webBuilder => webBuilder
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.ConfigureHttpJsonOptions(options =>
                        options.SerializerOptions.TypeInfoResolverChain.Insert(0,
                            BenchmarkJsonSerializerContext.Default));
                    services.AddSynapseAspNetCore();
                    services.AddSynapse(cfg =>
                    {
                        cfg.RegisterRequestHandler<GetThingQueryHandler, GetThingQuery, ThingDto>();
                        cfg.RegisterRequestHandler<CreateThingCommandHandler, CreateThingCommand, ThingDto>();
                        cfg.RegisterRequestHandler<SearchThingQueryHandler, SearchThingQuery, ThingDto>();
                        cfg.RegisterRequestHandler<SearchThingsQueryHandler, SearchThingsQuery, ThingDto>();
                    });

                    // Only the processor arm registers anything beyond the shared wiring, so every
                    // other arm's container is byte-for-byte what it was before this feature.
                    configureServices?.Invoke(services);
                })
                .Configure(configureApp))
            .Start();
    }
}

/// <summary>The response body both hosts serialize, via the same <see cref="BenchmarkJsonSerializerContext" />.</summary>
public sealed record ThingDto
{
    /// <summary>The thing's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The thing's name.</summary>
    public required string Name { get; init; }
}

/// <summary>
///     Gets one thing by id. <see cref="Id" /> is deliberately not <c>required</c>: the generated
///     binder for a route-only message constructs it with a bare <c>new GetThingQuery()</c> and then
///     applies the bound value via a <c>with</c> expression, which cannot satisfy a
///     <c>required</c> member.
/// </summary>
public sealed record GetThingQuery : IRequest<ThingDto>
{
    /// <summary>The thing's id, bound from the route.</summary>
    public Guid Id { get; init; }
}

/// <summary>
///     Handles <see cref="GetThingQuery" />. Shared, unmodified, by both benchmarked hosts: the
///     only thing that differs between them is how the request reaches this handler.
/// </summary>
public sealed class GetThingQueryHandler : IRequestHandler<GetThingQuery, ThingDto>
{
    /// <inheritdoc />
    public ValueTask<Result<ThingDto>> HandleAsync(GetThingQuery request,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Result.Success(new ThingDto { Id = request.Id, Name = "Widget" }));
    }
}

/// <summary>The Synapse endpoint side of the comparison, mapped via <c>MapEndpoint&lt;GetThingEndpoint&gt;()</c>.</summary>
[Get("/things/{id:guid}")]
public sealed partial class GetThingEndpoint : Endpoint<GetThingQuery, ThingDto>;

/// <summary>
///     The self-handled tier answering the same request without the mediator. Deliberately reuses
///     <see cref="GetThingQuery" /> — which it need not, since this tier requires no
///     <c>IRequest&lt;T&gt;</c> — so that it shares <see cref="GetThingEndpoint" />'s generated binder
///     exactly and the measured delta is dispatch alone.
/// </summary>
/// <remarks>
///     Carries the same route attribute for the same reason <see cref="HookedThingEndpoint" /> does.
/// </remarks>
[Get("/things/{id:guid}")]
public sealed partial class SelfHandledThingEndpoint : SelfHandledEndpoint<GetThingQuery, ThingDto>
{
    /// <inheritdoc />
    public override ValueTask<Result<ThingDto>> ExecuteAsync(GetThingQuery request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        // The body of GetThingQueryHandler, inlined: the arms differ only in how it is reached.
        return ValueTask.FromResult(Result.Success(new ThingDto { Id = request.Id, Name = "Widget" }));
    }
}

/// <summary>
///     The same endpoint with both hooks overridden, so the cost of an endpoint that actually uses
///     the seam is visible next to one that does not.
/// </summary>
/// <remarks>
///     Carries the same route attribute as <see cref="GetThingEndpoint" />: with no attribute at all,
///     the generator has no verb to resolve <see cref="GetThingQuery" />'s binding sources from
///     (SYNE014, a warning, and this repo builds with warnings as errors).
/// </remarks>
[Get("/things/{id:guid}")]
public sealed partial class HookedThingEndpoint : Endpoint<GetThingQuery, ThingDto>
{
    /// <inheritdoc />
    protected override ValueTask<IResult?> OnBeforeHandleAsync(GetThingQuery request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Items["thing-id"] = request.Id;
        return default;
    }

    /// <inheritdoc />
    protected override ValueTask<IResult> OnAfterHandleAsync(IResult result,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["X-Thing"] = "1";
        return new ValueTask<IResult>(result);
    }
}

/// <summary>The same endpoint with one pre- and one post-processor registered.</summary>
/// <remarks>Same route attribute as <see cref="HookedThingEndpoint" />, for the same reason.</remarks>
[Get("/things/{id:guid}")]
public sealed partial class ProcessedThingEndpoint : Endpoint<GetThingQuery, ThingDto>
{
    /// <inheritdoc />
    public override void Configure(IEndpointBuilder<ThingDto> builder)
    {
        builder.PreProcessor<BenchmarkPreProcessor>()
               .PostProcessor<BenchmarkPostProcessor>();
    }
}

/// <summary>A pre-processor that does the least possible work and never short-circuits.</summary>
public sealed class BenchmarkPreProcessor : IEndpointPreProcessor
{
    /// <inheritdoc />
    public ValueTask<IResult?> ProcessAsync(HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Items["pre"] = true;
        return default;
    }
}

/// <summary>A post-processor that does the least possible work and never replaces the result.</summary>
public sealed class BenchmarkPostProcessor : IEndpointPostProcessor
{
    /// <inheritdoc />
    public ValueTask<IResult> ProcessAsync(IResult result,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["X-Post"] = "1";
        return new ValueTask<IResult>(result);
    }
}

/// <summary>
///     A query bound from a single query value — the scalar counterpart of <see cref="SearchThingsQuery" />,
///     used to isolate query-string parsing cost from accumulation cost.
/// </summary>
public sealed record SearchThingQuery : IRequest<ThingDto>
{
    /// <summary>The tag to search by, bound from a single <c>tag</c> query value.</summary>
    [FromQuery(Name = "tag")]
    public string? Tag { get; init; }
}

/// <summary>Handles <see cref="SearchThingQuery" />. Never touches <see cref="SearchThingQuery.Tag" /> beyond binding it — this arm measures binding, not handling.</summary>
public sealed class SearchThingQueryHandler : IRequestHandler<SearchThingQuery, ThingDto>
{
    /// <inheritdoc />
    public ValueTask<Result<ThingDto>> HandleAsync(SearchThingQuery request,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Result.Success(new ThingDto { Id = Guid.Empty, Name = "Widget" }));
    }
}

/// <summary>The Synapse endpoint whose generated binder reads a single query value into <c>string?</c>.</summary>
[Get("/things")]
public sealed partial class SearchThingEndpoint : Endpoint<SearchThingQuery, ThingDto>;

/// <summary>
///     A query bound entirely from a repeated query key, exercising the generated collection binder
///     rather than <see cref="GetThingQuery" />'s single route scalar.
/// </summary>
public sealed record SearchThingsQuery : IRequest<ThingDto>
{
    /// <summary>The tags to search by, accumulated from every <c>tag</c> query value.</summary>
    [FromQuery(Name = "tag")]
    public string[] Tags { get; init; } = [];
}

/// <summary>Handles <see cref="SearchThingsQuery" />. Never touches <see cref="SearchThingsQuery.Tags" /> beyond binding it — this arm measures binding, not handling.</summary>
public sealed class SearchThingsQueryHandler : IRequestHandler<SearchThingsQuery, ThingDto>
{
    /// <inheritdoc />
    public ValueTask<Result<ThingDto>> HandleAsync(SearchThingsQuery request,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Result.Success(new ThingDto { Id = Guid.Empty, Name = "Widget" }));
    }
}

/// <summary>The Synapse endpoint whose generated binder reads a repeated query key into <c>string[]</c>.</summary>
[Get("/things")]
public sealed partial class SearchThingsEndpoint : Endpoint<SearchThingsQuery, ThingDto>;

/// <summary>
///     The low-level counterpart of <see cref="GetThingEndpoint" />: the same route, message, handler
///     and response mapping, with <c>BindAsync</c> written by hand rather than generated.
/// </summary>
[Get("/things/{id:guid}")]
public sealed partial class RawGetThingEndpoint : RawEndpoint<GetThingQuery, ThingDto>
{
    /// <inheritdoc />
    public override ValueTask<BindResult<GetThingQuery>> BindAsync(HttpContext context)
    {
        var validation = context.Validate();
        validation.Route<Guid>("id", out var id);

        return ValueTask.FromResult(validation.IsValid
            ? BindResult<GetThingQuery>.Success(new GetThingQuery { Id = id })
            : BindResult<GetThingQuery>.Failure(validation));
    }
}

/// <summary>Creates a thing from a JSON request body — the body-reading half of the comparison.</summary>
public sealed record CreateThingCommand : IRequest<ThingDto>
{
    /// <summary>The thing's name, bound from the request body.</summary>
    public string Name { get; init; } = "";
}

/// <summary>Handles <see cref="CreateThingCommand" />. Shared, unmodified, by both benchmarked hosts.</summary>
public sealed class CreateThingCommandHandler : IRequestHandler<CreateThingCommand, ThingDto>
{
    /// <inheritdoc />
    public ValueTask<Result<ThingDto>> HandleAsync(CreateThingCommand request,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Result.Success(new ThingDto { Id = Guid.Empty, Name = request.Name }));
    }
}

/// <summary>The Synapse endpoint whose generated binder reads the JSON request body.</summary>
[Post("/things")]
public sealed partial class CreateThingEndpoint : Endpoint<CreateThingCommand, ThingDto>;

/// <summary>
///     Source-generated JSON metadata so neither host pays for reflection-based serialization —
///     shared by both hosts via the common <see cref="EndpointDispatchBenchmark" /> setup.
/// </summary>
[JsonSerializable(typeof(ThingDto))]
[JsonSerializable(typeof(CreateThingCommand))]
internal sealed partial class BenchmarkJsonSerializerContext : JsonSerializerContext;
