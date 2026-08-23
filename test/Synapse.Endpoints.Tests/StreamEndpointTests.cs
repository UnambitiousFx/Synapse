using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class StreamEndpointTests
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

    private sealed record TickQuery : IStreamRequest<int>;

    private sealed class TickEndpoint : StreamEndpoint<TickQuery, int>;

    private sealed class TickBinder : IEndpointBinder<TickQuery>
    {
        public ValueTask<BindResult<TickQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<TickQuery>.Success(new TickQuery()));
        }
    }

    private sealed record ArrayQuery : IStreamRequest<int>;

    private sealed class ArrayEndpoint : StreamEndpoint<ArrayQuery, int>;

    private sealed class ArrayBinder : IEndpointBinder<ArrayQuery>
    {
        public ValueTask<BindResult<ArrayQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<ArrayQuery>.Success(new ArrayQuery()));
        }
    }

    private sealed record SseQuery : IStreamRequest<int>;

    private sealed class SseEndpoint : StreamEndpoint<SseQuery, int>;

    private sealed class SseBinder : IEndpointBinder<SseQuery>
    {
        public ValueTask<BindResult<SseQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<SseQuery>.Success(new SseQuery()));
        }
    }
}

[JsonSerializable(typeof(int))]
internal sealed partial class StreamTestJsonContext : JsonSerializerContext;
