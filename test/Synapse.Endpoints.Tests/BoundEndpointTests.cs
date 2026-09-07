using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Abstractions;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class BoundEndpointTests
{
    [Theory]
    [InlineData(typeof(RawEndpoint<PingCommand>))]
    [InlineData(typeof(RawEndpoint<PingQuery, string>))]
    [InlineData(typeof(MappedEndpoint<PingDto, PingQuery, string, string>))]
    [InlineData(typeof(StreamEndpoint<PingStream, string>))]
    public void EveryBoundTier_DerivesFromBoundEndpoint(Type tier)
    {
        // Arrange: the four sealed tiers must share one declaration of the hooks, or the ordering
        // contract exists in four places and can drift.

        // Act
        var derivesFromBoundEndpoint = Walk(tier)
            .Any(type => type.IsGenericType &&
                         type.GetGenericTypeDefinition() == typeof(BoundEndpoint<>));

        // Assert
        Assert.True(derivesFromBoundEndpoint, $"{tier} does not derive from BoundEndpoint<>.");

        static IEnumerable<Type> Walk(Type type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                yield return current;
            }
        }
    }

    [Fact]
    public async Task HandleAsync_OnAnUnmappedEndpoint_PointsAtTheTestHarness()
    {
        // Arrange: the processors are now read before the configuration is, so this message has to
        // survive that reordering — see docs/known-issues/056.
        var endpoint = new UnmappedEndpoint();

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await endpoint.HandleAsync(new DefaultHttpContext(), CancellationToken.None));

        // Assert
        Assert.Contains("EndpointHarness.Create", exception.Message);
        Assert.Contains("UnambitiousFx.Synapse.Endpoints.Testing", exception.Message);
    }

    [Fact]
    public void BoundEndpoint_HasNoConstructorAccessibleOutsideTheLibrary()
    {
        // Arrange: it is a shared seam, not a sixth tier, so it must not be derivable by consumers.

        // Act
        var constructors = typeof(BoundEndpoint<>).GetConstructors(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);

        // Assert
        Assert.All(constructors, constructor =>
            Assert.True(constructor.IsFamilyAndAssembly,
                "The constructor must be private protected so the type cannot be derived from " +
                "outside this assembly."));
    }

    public sealed record PingCommand : IRequest;

    public sealed record PingQuery : IRequest<string>;

    public sealed record PingDto;

    public sealed record PingStream : IStreamRequest<string>;

    private sealed partial class UnmappedEndpoint : Endpoint<PingQuery, string>;
}
