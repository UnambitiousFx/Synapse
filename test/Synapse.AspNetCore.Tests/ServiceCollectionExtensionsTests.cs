using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.AspNetCore.Mvc;

namespace UnambitiousFx.Synapse.AspNetCore.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSynapseAspNetCore_RegistersExpectedServices()
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When)
        services.AddSynapseAspNetCore();

        // Assert (Then)
        Assert.Contains(services, x =>
            x.ServiceType == typeof(IFailureHttpMapper) &&
            x.ImplementationType == typeof(DefaultFailureHttpMapper) &&
            x.Lifetime == ServiceLifetime.Singleton);

        Assert.Contains(services, x =>
            x.ServiceType == typeof(IHttpInvoker) &&
            x.ImplementationType == typeof(HttpInvoker) &&
            x.Lifetime == ServiceLifetime.Scoped);

        Assert.Contains(services, x =>
            x.ServiceType == typeof(IMvcInvoker) &&
            x.ImplementationType == typeof(MvcInvoker) &&
            x.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddSynapseAspNetCore_WhenCalledTwice_DoesNotDuplicateRegistrations()
    {
        // Arrange (Given)
        var services = new ServiceCollection();

        // Act (When)
        services.AddSynapseAspNetCore();
        services.AddSynapseAspNetCore();

        // Assert (Then)
        Assert.Equal(1, services.Count(x => x.ServiceType == typeof(IFailureHttpMapper)));
        Assert.Equal(1, services.Count(x => x.ServiceType == typeof(IHttpInvoker)));
        Assert.Equal(1, services.Count(x => x.ServiceType == typeof(IMvcInvoker)));
    }
}
