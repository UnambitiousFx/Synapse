using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class JsonTypeInfoCacheTests
{
    [Fact]
    public void Get_WhenCalledTwice_ReturnsTheSameCachedInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, CacheTestJsonContext.Default));
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var cache = new JsonTypeInfoCache<CachePayload>();

        // Act
        var first = cache.Get(context);
        var second = cache.Get(context);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(typeof(CachePayload), first.Type);
    }

    [Fact]
    public void Get_WhenAlreadyResolved_DoesNotTouchRequestServicesAgain()
    {
        // Arrange
        var services = new ServiceCollection();
        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, CacheTestJsonContext.Default));
        var cache = new JsonTypeInfoCache<CachePayload>();
        var first = cache.Get(new DefaultHttpContext { RequestServices = services.BuildServiceProvider() });

        // Act
        var second = cache.Get(new DefaultHttpContext { RequestServices = new ThrowingServiceProvider() });

        // Assert
        Assert.Same(first, second);
    }

    // The bug this exists to prevent: the body path used to hold its JsonTypeInfo in a
    // `static class BodyTypeInfo<T>` holder, which is one entry per closed T per *process*. Two hosts
    // in one process (two WebApplicationFactory instances in a single test run) with different
    // ConfigureHttpJsonOptions silently shared whichever resolved first — and ReadFromJsonAsync
    // serializes with the type info's own options, so the second host's configuration was ignored
    // outright rather than merely arriving late. Keyed on the options instance, each application gets
    // its own.
    [Fact]
    public void Resolve_ForTwoApplications_ResolvesFromEachApplicationsOwnOptions()
    {
        // Arrange
        var first = BuildApplication();
        var second = BuildApplication();

        // Act
        var firstInfo = HttpJsonTypeInfo.Resolve<CachePayload>(
            new DefaultHttpContext { RequestServices = first });
        var secondInfo = HttpJsonTypeInfo.Resolve<CachePayload>(
            new DefaultHttpContext { RequestServices = second });

        // Assert
        Assert.NotSame(firstInfo, secondInfo);
        Assert.Same(SerializerOptionsOf(first), firstInfo.Options);
        Assert.Same(SerializerOptionsOf(second), secondInfo.Options);
    }

    [Fact]
    public void Resolve_ForTheSameApplicationTwice_ReturnsTheCachedInstance()
    {
        // Arrange — the caching half of the same claim: keying per application must not turn into
        // resolving afresh on every request.
        var application = BuildApplication();

        // Act
        var firstInfo = HttpJsonTypeInfo.Resolve<CachePayload>(
            new DefaultHttpContext { RequestServices = application });
        var secondInfo = HttpJsonTypeInfo.Resolve<CachePayload>(
            new DefaultHttpContext { RequestServices = application });

        // Assert
        Assert.Same(firstInfo, secondInfo);
    }

    private static ServiceProvider BuildApplication()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, CacheTestJsonContext.Default));
        return services.BuildServiceProvider();
    }

    private static JsonSerializerOptions SerializerOptionsOf(IServiceProvider provider)
    {
        return provider.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
    }

    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            throw new InvalidOperationException(
                "RequestServices must not be resolved again once the type info is cached.");
        }
    }
}

internal sealed record CachePayload(string Name);

[JsonSerializable(typeof(CachePayload))]
internal sealed partial class CacheTestJsonContext : JsonSerializerContext;
