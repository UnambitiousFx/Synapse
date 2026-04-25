using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.AspNetCore.Tests;

public sealed class ApplicationBuilderExtensionsTests
{
    [Fact]
    public async Task UseCorrelationId_WithValidIncomingHeader_UpdatesContextCorrelationId()
    {
        // Arrange (Given)
        var incoming = Guid.NewGuid();
        var originalContext = Substitute.For<IContext>();
        var updatedContext = Substitute.For<IContext>();
        updatedContext.CorrelationId.Returns(incoming);
        originalContext.WithCorrelationId(incoming).Returns(updatedContext);

        var holder = new TestContextHolder { Context = originalContext };
        var services = new ServiceCollection();
        services.AddSingleton<IContextSetter>(holder);
        using var serviceProvider = services.BuildServiceProvider();

        var pipeline = BuildPipeline(serviceProvider);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        httpContext.Request.Headers.Append("X-Correlation-Id", incoming.ToString());

        // Act (When)
        await pipeline(httpContext);

        // Assert (Then)
        Assert.Same(updatedContext, holder.Context);
    }

    [Fact]
    public async Task UseCorrelationId_WhenContextAccessorIsAvailable_WritesResponseHeaderOnStarting()
    {
        // Arrange (Given)
        var correlationId = Guid.NewGuid();
        var context = Substitute.For<IContext>();
        context.CorrelationId.Returns(correlationId);

        var holder = new TestContextHolder { Context = context };
        var services = new ServiceCollection();
        services.AddSingleton<IContextAccessor>(holder);
        using var serviceProvider = services.BuildServiceProvider();

        var pipeline = BuildPipeline(serviceProvider);
        var responseFeature = new TestResponseFeature();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        httpContext.Features.Set<IHttpResponseFeature>(responseFeature);

        // Act (When)
        await pipeline(httpContext);
        await responseFeature.InvokeOnStartingAsync();

        // Assert (Then)
        Assert.True(httpContext.Response.Headers.TryGetValue("X-Correlation-Id", out var header));
        Assert.Equal(correlationId.ToString(), header.ToString());
    }

    [Fact]
    public async Task UseCorrelationId_WithInvalidIncomingHeader_DoesNotMutateContext()
    {
        // Arrange (Given)
        var originalContext = Substitute.For<IContext>();
        var holder = new TestContextHolder { Context = originalContext };
        var services = new ServiceCollection();
        services.AddSingleton<IContextSetter>(holder);
        using var serviceProvider = services.BuildServiceProvider();

        var pipeline = BuildPipeline(serviceProvider);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        httpContext.Request.Headers.Append("X-Correlation-Id", "not-a-guid");

        // Act (When)
        await pipeline(httpContext);

        // Assert (Then)
        Assert.Same(originalContext, holder.Context);
    }

    private static RequestDelegate BuildPipeline(IServiceProvider serviceProvider)
    {
        var app = new ApplicationBuilder(serviceProvider);
        app.UseCorrelationId();
        app.Run(ctx => ctx.Response.WriteAsync("ok"));
        return app.Build();
    }

    private sealed class TestContextHolder : IContextAccessor, IContextSetter
    {
        public IContext Context { get; set; } = default!;
    }

    private sealed class TestResponseFeature : IHttpResponseFeature
    {
        private readonly Stack<(Func<object, Task> Callback, object State)> _onStartingCallbacks = new();

        public int StatusCode { get; set; }
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state)
        {
            _onStartingCallbacks.Push((callback, state));
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public async Task InvokeOnStartingAsync()
        {
            while (_onStartingCallbacks.Count > 0)
            {
                var callback = _onStartingCallbacks.Pop();
                await callback.Callback(callback.State);
            }

            HasStarted = true;
        }
    }
}
