using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions.Exceptions;
using UnambitiousFx.Synapse.Resolvers;

namespace UnambitiousFx.Synapse.Tests.Resolvers;

public sealed class DefaultDependencyResolverTests
{
    [Fact]
    public void GetService_WhenServiceExists_ReturnsInstance()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSingleton<TestService>();
        using var provider = services.BuildServiceProvider();
        var resolver = new DefaultDependencyResolver(provider);

        // Act (When)
        var service = resolver.GetService<TestService>();

        // Assert (Then)
        Assert.NotNull(service);
    }

    [Fact]
    public void GetRequiredService_WhenServiceMissing_ThrowsMissingHandlerException()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var resolver = new DefaultDependencyResolver(provider);

        // Act (When)
        var action = () => resolver.GetRequiredService<TestService>();

        // Assert (Then)
        Assert.Throws<MissingHandlerException>(action);
    }

    [Fact]
    public void GetServices_WhenMultipleRegistered_ReturnsAllInstances()
    {
        // Arrange (Given)
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, FirstTestService>();
        services.AddSingleton<ITestService, SecondTestService>();
        using var provider = services.BuildServiceProvider();
        var resolver = new DefaultDependencyResolver(provider);

        // Act (When)
        var all = resolver.GetServices<ITestService>().ToList();

        // Assert (Then)
        Assert.Equal(2, all.Count);
    }

    private sealed class TestService;

    private interface ITestService;
    private sealed class FirstTestService : ITestService;
    private sealed class SecondTestService : ITestService;
}
