using System.Text;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

namespace UnambitiousFx.Benchmarks.SynapseBenchmark;

/// <summary>
///     Compares dispatching one request through a hand-written Minimal API lambda against the
///     equivalent Synapse endpoint, so the adapter's overhead stays visible.
/// </summary>
/// <remarks>
///     Both hosts share the exact same <see cref="BuildHost" /> service configuration — same
///     <see cref="GetThingQueryHandler" />, same <c>ConfigureHttpJsonOptions</c> call with the same
///     source-generated <see cref="BenchmarkJsonSerializerContext" />, same <c>AddSynapseAspNetCore</c>
///     / <c>AddSynapse</c> wiring — so the only thing that differs between
///     <see cref="HandWrittenLambda" /> and <see cref="SynapseEndpoint" /> is how the route is mapped:
///     a hand-rolled <c>MapGet</c> lambda with typed parameters versus <c>MapEndpoint&lt;GetThingEndpoint&gt;()</c>,
///     which goes through the generated route binder and <c>Endpoint&lt;TRequest,TResponse&gt;</c>'s
///     descriptor. Both call sides use the identical three-argument
///     <c>IHttpInvoker.InvokeAsync(request, response =&gt; TypedResults.Ok(response), cancellationToken)</c>
///     overload with an equivalent success mapper, matching what <c>Endpoint&lt;TRequest,TResponse&gt;</c>'s
///     default <c>OnSuccess</c> does, so no part of the delta is attributable to calling a different
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
    private HttpClient _handWritten = null!;
    private HttpClient _endpoint = null!;
    private HttpClient _raw = null!;

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

        _handWritten = _handWrittenHost.GetTestServer().CreateClient();
        _endpoint = _endpointHost.GetTestServer().CreateClient();
        _raw = _rawHost.GetTestServer().CreateClient();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _handWritten.Dispose();
        _endpoint.Dispose();
        _raw.Dispose();
        _handWrittenHost.Dispose();
        _endpointHost.Dispose();
        _rawHost.Dispose();
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
    ///     Builds and starts one in-memory host. Both benchmarked hosts call this same method, so the
    ///     DI wiring — JSON options, <c>AddSynapseAspNetCore</c>, <c>AddSynapse</c>, the registered
    ///     handler — is not just equivalent between them, it is the identical code path. Only the
    ///     <paramref name="configureApp" /> delegate (how the route is mapped) differs.
    /// </summary>
    private static IHost BuildHost(Action<IApplicationBuilder> configureApp)
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
                    });
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
public sealed class GetThingEndpoint : Endpoint<GetThingQuery, ThingDto>;

/// <summary>
///     The low-level counterpart of <see cref="GetThingEndpoint" />: the same route, message, handler
///     and response mapping, with <c>BindAsync</c> written by hand rather than generated.
/// </summary>
[Get("/things/{id:guid}")]
public sealed class RawGetThingEndpoint : RawEndpoint<GetThingQuery, ThingDto>
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
public sealed class CreateThingEndpoint : Endpoint<CreateThingCommand, ThingDto>;

/// <summary>
///     Source-generated JSON metadata so neither host pays for reflection-based serialization —
///     shared by both hosts via the common <see cref="EndpointDispatchBenchmark" /> setup.
/// </summary>
[JsonSerializable(typeof(ThingDto))]
[JsonSerializable(typeof(CreateThingCommand))]
internal sealed partial class BenchmarkJsonSerializerContext : JsonSerializerContext;
