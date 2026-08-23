using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
