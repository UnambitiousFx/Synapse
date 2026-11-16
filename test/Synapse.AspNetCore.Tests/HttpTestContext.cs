using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse.AspNetCore.Tests;

/// <summary>
///     A <see cref="DefaultHttpContext" /> whose response runs its <c>OnStarting</c> callbacks, which the default
///     feature silently drops. Call <see cref="StartAsync" /> after executing a result to simulate the server flushing.
/// </summary>
internal sealed class HttpTestContext
{
    private readonly StartingResponseFeature _feature = new();

    public HttpTestContext(Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection().AddLogging();
        configureServices?.Invoke(services);

        Context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        Context.Features.Set<IHttpResponseFeature>(_feature);
        Context.Response.Body = new MemoryStream();
    }

    public DefaultHttpContext Context { get; }

    public HttpResponse Response => Context.Response;

    public string Body()
    {
        Context.Response.Body.Position = 0;
        return new StreamReader(Context.Response.Body).ReadToEnd();
    }

    public async Task StartAsync()
    {
        foreach (var (callback, state) in _feature.Callbacks)
        {
            await callback(state);
        }
    }

    private sealed class StartingResponseFeature : HttpResponseFeature
    {
        public List<(Func<object, Task> Callback, object State)> Callbacks { get; } = [];

        public override void OnStarting(Func<object, Task> callback, object state)
        {
            Callbacks.Add((callback, state));
        }
    }
}
