using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

/// <summary>
///     What the registry still holds: route metadata. The two binder tests that used to live here are
///     gone with the binder half of it — a binding is now an <c>override</c> the compiler requires, so
///     there is no lookup left to test and no "not registered" state left to reach.
/// </summary>
public sealed partial class EndpointRegistryTests
{
    [Fact]
    public void GetMetadata_WhenRegistered_ReturnsTheRegisteredMetadata()
    {
        // Arrange
        var metadata = new EndpointMetadata(["GET"], "/registry-probe");
        EndpointRegistry.RegisterMetadata<ProbeEndpoint>(metadata);

        // Act
        var resolved = EndpointRegistry.GetMetadata<ProbeEndpoint>();

        // Assert
        Assert.Same(metadata, resolved);
    }

    [Fact]
    public void RegisterMetadata_WithNullMetadata_Throws()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => EndpointRegistry.RegisterMetadata<ProbeEndpoint>(null!));
    }

    internal sealed record ProbeQuery : IRequest<string>;

    [Get("/registry-probe")]
    internal sealed partial class ProbeEndpoint : Endpoint<ProbeQuery, string>;
}
