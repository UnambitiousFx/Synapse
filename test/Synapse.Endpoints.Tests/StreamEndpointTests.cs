using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class StreamEndpointTests
{
    [Theory]
    [InlineData("text/event-stream", "text/event-stream")]
    [InlineData("application/json", "application/json")]
    [InlineData(null, "application/json")]
    public async Task Invoke_NegotiatesContentType(string? accept, string expectedContentType)
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new TickBinder());
        EndpointRegistry.RegisterMetadata<TickEndpoint>(new EndpointMetadata(["GET"], "/ticks"));

        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeStreamAsync(Arg.Any<IStreamRequest<int>>(), Arg.Any<CancellationToken>())
            .Returns(Ticks());

        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();
        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, StreamTestJsonContext.Default));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        if (accept is not null)
        {
            context.Request.Headers.Accept = accept;
        }

        var descriptor = ((EndpointBase)new TickEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<TickEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.StartsWith(expectedContentType, context.Response.ContentType);

        static async IAsyncEnumerable<int> Ticks()
        {
            yield return 1;
            await Task.Yield();
            yield return 2;
        }
    }

    [Fact]
    public async Task Invoke_WithJsonAccept_WritesExactJsonArrayBody()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new ArrayBinder());
        EndpointRegistry.RegisterMetadata<ArrayEndpoint>(new EndpointMetadata(["GET"], "/array-ticks"));

        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeStreamAsync(Arg.Any<IStreamRequest<int>>(), Arg.Any<CancellationToken>())
            .Returns(Ticks());

        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();
        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, StreamTestJsonContext.Default));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        context.Request.Headers.Accept = "application/json";

        var descriptor = ((EndpointBase)new ArrayEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<ArrayEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal("[1,2]", body);

        static async IAsyncEnumerable<int> Ticks()
        {
            yield return 1;
            await Task.Yield();
            yield return 2;
        }
    }

    [Fact]
    public async Task Invoke_WithEventStreamAccept_WritesServerSentEventBody()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new SseBinder());
        EndpointRegistry.RegisterMetadata<SseEndpoint>(new EndpointMetadata(["GET"], "/sse-ticks"));

        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeStreamAsync(Arg.Any<IStreamRequest<int>>(), Arg.Any<CancellationToken>())
            .Returns(Ticks());

        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();
        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, StreamTestJsonContext.Default));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        context.Request.Headers.Accept = "text/event-stream";

        var descriptor = ((EndpointBase)new SseEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<SseEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Contains("data: 1\n\n", body);

        static async IAsyncEnumerable<int> Ticks()
        {
            yield return 1;
            await Task.Yield();
            yield return 2;
        }
    }

    // The stream tier configures through IStreamEndpointBuilder, which is IEndpointBuilder minus the
    // success-mapping methods. Those set a mapper this class never consults — a stream's status is
    // committed before the first item — so they used to compile here and silently do nothing. Asserted
    // by reflection so re-introducing one is a failing test rather than a returning trap. See
    // docs/known-issues/064.
    [Theory]
    [InlineData("NoContent")]
    [InlineData("StatusCode")]
    [InlineData("Ok")]
    [InlineData("Created")]
    [InlineData("Accepted")]
    public void StreamEndpointBuilder_OffersNoSuccessMapping(string member)
    {
        // Act
        var members = typeof(IStreamEndpointBuilder).GetMembers().Select(m => m.Name).ToArray();

        // Assert
        Assert.DoesNotContain(member, members);
    }

    // What it does still offer, so the narrowing did not take anything useful with it.
    [Theory]
    [InlineData("Get")]
    [InlineData("Post")]
    [InlineData("Route")]
    [InlineData("Tag")]
    [InlineData("Summary")]
    [InlineData("Description")]
    [InlineData("Name")]
    [InlineData("RequireAuthorization")]
    [InlineData("AllowAnonymous")]
    [InlineData("Raw")]
    public void StreamEndpointBuilder_KeepsRoutingAndMetadata(string member)
    {
        // Act
        var members = typeof(IStreamEndpointBuilder).GetMembers().Select(m => m.Name).ToArray();

        // Assert
        Assert.Contains(member, members);
    }

    // The new builder owns route resolution for this tier, so the "route declared in Configure" path
    // has to keep working through it.
    [Fact]
    public void CreateDescriptor_ForAStreamEndpointDeclaringItsRouteInConfigure_ResolvesIt()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new TickBinder());
        EndpointRegistry.RegisterMetadata<ConfiguredStreamEndpoint>(
            new EndpointMetadata([], string.Empty));

        // Act
        var descriptor = ((EndpointBase)new ConfiguredStreamEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<ConfiguredStreamEndpoint>());

        // Assert
        Assert.Equal("/computed-ticks", descriptor.Route);
        Assert.Equal(["GET"], descriptor.HttpMethods);
    }

    internal sealed partial class ConfiguredStreamEndpoint : StreamEndpoint<TickQuery, int>
    {
        public override void Configure(IStreamEndpointBuilder builder)
        {
            builder.Get("/computed-ticks").Tag("Ticks");
        }
    }

    internal sealed record TickQuery : IStreamRequest<int>;

    internal sealed partial class TickEndpoint : StreamEndpoint<TickQuery, int>;

    private sealed class TickBinder : IEndpointBinder<TickQuery>
    {
        public ValueTask<BindResult<TickQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<TickQuery>.Success(new TickQuery()));
        }
    }

    internal sealed record ArrayQuery : IStreamRequest<int>;

    internal sealed partial class ArrayEndpoint : StreamEndpoint<ArrayQuery, int>;

    private sealed class ArrayBinder : IEndpointBinder<ArrayQuery>
    {
        public ValueTask<BindResult<ArrayQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<ArrayQuery>.Success(new ArrayQuery()));
        }
    }

    internal sealed record SseQuery : IStreamRequest<int>;

    internal sealed partial class SseEndpoint : StreamEndpoint<SseQuery, int>;

    private sealed class SseBinder : IEndpointBinder<SseQuery>
    {
        public ValueTask<BindResult<SseQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<SseQuery>.Success(new SseQuery()));
        }
    }
}

// SYNE008 (unused before the generator ran here) checks every request/response type against the
// JsonSerializerContext(s) in the compilation; the response and DTO types below are otherwise only
// ever round-tripped through reflection-based JsonSerializer calls in these tests, so they had never
// been registered anywhere. The request types in the second group joined the list when their
// endpoints gained the route attributes that keep SYNE014 quiet: a POST attribute is what makes the
// generator resolve their properties to the request body in the first place.
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(MappedEndpointTests.CreateResponse))]
[JsonSerializable(typeof(MappedEndpointTests.CreatedResponse))]
[JsonSerializable(typeof(MappedEndpointTests.FailingCreateResponse))]
[JsonSerializable(typeof(OpenApiMetadataTests.CreatedMappedResponse))]
[JsonSerializable(typeof(RawEndpointTests.Greeting))]
[JsonSerializable(typeof(SelfHandledEndpointTests.ProbeDto))]
[JsonSerializable(typeof(EndpointLifecycleTests.TraceWireRequest))]
[JsonSerializable(typeof(MappedEndpointTests.CreateBody))]
[JsonSerializable(typeof(OpenApiMetadataTests.CreatedMappedRequest))]
[JsonSerializable(typeof(OpenApiMetadataTests.MetaQuery))]
[JsonSerializable(typeof(OpenApiMetadataTests.MultiVerbMetaQuery))]
[JsonSerializable(typeof(OpenApiMetadataTests.SelfHandledMetaRequest))]
[JsonSerializable(typeof(OpenApiMetadataTests.SelfHandledVoidMetaRequest))]
[JsonSerializable(typeof(SelfHandledEndpointTests.AcceptedProbeQuery))]
[JsonSerializable(typeof(SelfHandledEndpointTests.CreatedProbeQuery))]
[JsonSerializable(typeof(SelfHandledEndpointVoidTests.QueuedRequest))]
internal sealed partial class StreamTestJsonContext : JsonSerializerContext;
