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
}

internal sealed record CachePayload(string Name);

[JsonSerializable(typeof(CachePayload))]
internal sealed partial class CacheTestJsonContext : JsonSerializerContext;
