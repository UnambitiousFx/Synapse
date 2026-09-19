using Microsoft.AspNetCore.Http;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed partial class RawEndpointHarnessTests
{
    [Fact]
    public async Task SendAsync_ForARawEndpoint_ReturnsItsStatusAndBody()
    {
        // Arrange
        using var harness = EndpointHarness.Create<PingEndpoint>();

        // Act
        var response = await harness.Get("/ping").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("\"pong\"", response.Body);
    }

    [Fact]
    public async Task SendAsync_ForARawEndpoint_RunsConfigureExactlyOnce()
    {
        // Arrange: Configure runs at map time, so a harness that mapped twice - or that
        // re-mapped per request - would show up here rather than as a subtle metadata bug.
        CountingEndpoint.ConfigureCount = 0;
        using var harness = EndpointHarness.Create<CountingEndpoint>();

        // Act
        await harness.Get("/counting").SendAsync(TestContext.Current.CancellationToken);
        await harness.Get("/counting").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, CountingEndpoint.ConfigureCount);
    }

    [Get("/ping")]
    internal sealed partial class PingEndpoint : RawEndpoint
    {
        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IResult>(TypedResults.Ok("pong"));
        }
    }

    [Get("/counting")]
    internal sealed partial class CountingEndpoint : RawEndpoint
    {
        internal static int ConfigureCount;

        public override void Configure(Builders.IRawEndpointBuilder builder)
        {
            ConfigureCount++;
        }

        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IResult>(TypedResults.NoContent());
        }
    }
}
