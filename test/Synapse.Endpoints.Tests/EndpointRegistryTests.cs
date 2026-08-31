using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class EndpointRegistryTests
{
    [Fact]
    public void GetBinder_WhenRegistered_ReturnsTheRegisteredBinder()
    {
        // Arrange
        var binder = new StubBinder();
        EndpointRegistry.RegisterBinder(binder);

        // Act
        var resolved = EndpointRegistry.GetBinder<RegisteredRequest>();

        // Assert
        Assert.Same(binder, resolved);
    }

    [Fact]
    public void GetBinder_WhenNotRegistered_ThrowsMentioningTheAnalyzer()
    {
        // Arrange & Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => EndpointRegistry.GetBinder<UnregisteredRequest>());

        // Assert
        Assert.Contains("UnregisteredRequest", exception.Message);
        Assert.Contains("analyzer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RegisteredRequest : IRequest;

    private sealed record UnregisteredRequest : IRequest;

    private sealed class StubBinder : IEndpointBinder<RegisteredRequest>
    {
        public ValueTask<BindResult<RegisteredRequest>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<RegisteredRequest>.Success(new RegisteredRequest()));
        }
    }
}
