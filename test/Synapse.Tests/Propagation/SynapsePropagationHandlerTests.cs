using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Contexts;
using UnambitiousFx.Synapse.Propagation;

namespace UnambitiousFx.Synapse.Tests.Propagation;

public sealed class SynapsePropagationHandlerTests
{
    [Fact]
    public async Task SendAsync_WhenAContextExists_StampsBaggageOntoTheRequest()
    {
        // Arrange (Given)
        var context = NewContext();
        context.SetBaggage("tenant.id", "contoso");
        using var ambient = Publish(context);
        var handler = BuildHandler(out _);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/orders");

        // Act (When)
        using var invoker = new HttpMessageInvoker(handler);
        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then) — business values only; identity travels in traceparent, written by the platform
        Assert.True(request.Headers.TryGetValues(PropagationKeys.Baggage, out var baggage));
        var header = string.Join(',', baggage!);
        Assert.Equal("tenant.id=contoso", header);
        Assert.DoesNotContain("synapse.", header, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WhenNoContextExists_SendsNothingExtra()
    {
        // Arrange (Given) — an outbound call outside any unit of work must not invent a flow
        Assert.Null(AmbientContext.Value);
        var handler = BuildHandler(out _);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/health");

        // Act (When)
        using var invoker = new HttpMessageInvoker(handler);
        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.False(request.Headers.Contains(PropagationKeys.Baggage));
    }

    [Fact]
    public async Task SendAsync_WhenCalledTwice_DoesNotAccumulateDuplicateHeaders()
    {
        // Arrange (Given) — a retrying handler above this one re-sends the same request instance
        var context = NewContext();
        context.SetBaggage("tenant.id", "contoso");
        using var ambient = Publish(context);
        var handler = BuildHandler(out _);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/orders");

        // Act (When)
        using var invoker = new HttpMessageInvoker(handler);
        (await invoker.SendAsync(request, TestContext.Current.CancellationToken)).Dispose();
        (await invoker.SendAsync(request, TestContext.Current.CancellationToken)).Dispose();

        // Assert (Then)
        Assert.Single(request.Headers.GetValues(PropagationKeys.Baggage));
    }

    [Fact]
    public async Task SendAsync_ForwardsToTheInnerHandler()
    {
        // Arrange (Given)
        using var ambient = Publish(NewContext());
        var handler = BuildHandler(out var inner);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/orders");

        // Act (When)
        using var invoker = new HttpMessageInvoker(handler);
        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Equal(1, inner.CallCount);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WhenBuiltOutsideTheScopeDoingTheWork_StillStampsBaggage()
    {
        // Arrange (Given) — this is what IHttpClientFactory does: it constructs message handlers in a scope of
        // its own and caches them, so the handler can never be built from the scope that later makes the call.
        // Injecting a scoped IContextAccessor made this case silently stamp nothing (known issue 033).
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSynapse(_ => { })
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        using var factoryScope = provider.CreateScope();
        var handler = factoryScope.ServiceProvider.GetRequiredService<SynapsePropagationHandler>();
        handler.InnerHandler = new RecordingHandler();

        using var workScope = provider.CreateScope();
        var context = workScope.ServiceProvider.GetRequiredService<IContext>();
        context.SetBaggage("tenant.id", "contoso");

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/orders");

        // Act (When)
        using var invoker = new HttpMessageInvoker(handler);
        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Equal("tenant.id=contoso",
            string.Join(',', request.Headers.GetValues(PropagationKeys.Baggage)));
    }

    [Fact]
    public void Handler_ResolvesFromTheRootProvider_WithScopeValidationEnabled()
    {
        // Arrange (Given) — the handler is transient and IHttpClientFactory resolves it outside any request
        // scope, so depending on a scoped service is not merely useless, it is a captive dependency.
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSynapse(_ => { })
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        // Act (When)
        var resolve = () => provider.GetRequiredService<SynapsePropagationHandler>();

        // Assert (Then)
        using var handler = resolve();
        Assert.NotNull(handler);
    }

    private static SynapsePropagationHandler BuildHandler(out RecordingHandler inner)
    {
        inner = new RecordingHandler();
        return new SynapsePropagationHandler(new W3CContextPropagator())
        {
            InnerHandler = inner
        };
    }

    private static AmbientScope Publish(IContext context)
    {
        return new AmbientScope(AmbientContext.Exchange(context));
    }

    private static Context NewContext()
    {
        var identity = new ContextIdentity(ActivityTraceId.CreateRandom().ToHexString(), null,
            DateTimeOffset.UtcNow);
        return new Context(identity);
    }

    private sealed class AmbientScope : IDisposable
    {
        private readonly IContext? _previous;

        public AmbientScope(IContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            AmbientContext.Exchange(_previous);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
