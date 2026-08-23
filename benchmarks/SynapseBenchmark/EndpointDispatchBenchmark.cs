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

    private IHost _handWrittenHost = null!;
    private IHost _endpointHost = null!;
    private HttpClient _handWritten = null!;
    private HttpClient _endpoint = null!;

    [GlobalSetup]
    public void Setup()
    {
        _handWrittenHost = BuildHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
                endpoints.MapGet("/things/{id:guid}",
                    (Guid id, IHttpInvoker invoker, CancellationToken ct) =>
                        invoker.InvokeAsync(new GetThingQuery { Id = id },
                            response => TypedResults.Ok(response),
                            ct)));
        });

        _endpointHost = BuildHost(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapEndpoint<GetThingEndpoint>());
        });

        _handWritten = _handWrittenHost.GetTestServer().CreateClient();
        _endpoint = _endpointHost.GetTestServer().CreateClient();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _handWritten.Dispose();
        _endpoint.Dispose();
        _handWrittenHost.Dispose();
        _endpointHost.Dispose();
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
                        cfg.RegisterRequestHandler<GetThingQueryHandler, GetThingQuery, ThingDto>());
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
///     Source-generated JSON metadata so neither host pays for reflection-based serialization —
///     shared by both hosts via the common <see cref="EndpointDispatchBenchmark" /> setup.
/// </summary>
[JsonSerializable(typeof(ThingDto))]
internal sealed partial class BenchmarkJsonSerializerContext : JsonSerializerContext;
