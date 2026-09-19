using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UnambitiousFx.Synapse.Endpoints.OpenApi.Internal;

namespace UnambitiousFx.Synapse.Endpoints.OpenApi.Tests;

public sealed class OpenApiServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSynapseEndpointsOpenApi_WithNullServices_Throws()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => OpenApiServiceCollectionExtensions.AddSynapseEndpointsOpenApi(null!));
    }

    [Fact]
    public void AddSynapseEndpointsOpenApi_ReturnsServicesForChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddSynapseEndpointsOpenApi();

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void AddSynapseEndpointsOpenApi_RegistersBothTheFixupAndTheOperationTransformer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSynapseEndpointsOpenApi();

        // Assert — the fixup (an IApiDescriptionProvider) and the transformer's ConfigureAll
        // registration (an IConfigureOptions<OpenApiOptions>) both land from the one call.
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IApiDescriptionProvider) &&
            d.ImplementationType == typeof(FormRequestBodyDescriptionFixup));
        Assert.Contains(services, d => d.ServiceType == typeof(IConfigureOptions<OpenApiOptions>));
    }

    [Fact]
    public void AddSynapseEndpointsOpenApi_CalledTwice_RegistersEachExactlyOnce()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSynapseEndpointsOpenApi();
        services.AddSynapseEndpointsOpenApi();

        // Assert — a second call must not add a second fixup or a second operation transformer;
        // ConfigureAll has no built-in guard against that, so the extension has to supply one.
        Assert.Single(services, d =>
            d.ServiceType == typeof(IApiDescriptionProvider) &&
            d.ImplementationType == typeof(FormRequestBodyDescriptionFixup));
        Assert.Single(services, d => d.ServiceType == typeof(IConfigureOptions<OpenApiOptions>));
    }
}
