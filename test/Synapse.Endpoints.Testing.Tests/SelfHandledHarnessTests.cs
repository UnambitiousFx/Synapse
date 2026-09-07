using Microsoft.AspNetCore.Http;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed partial class SelfHandledHarnessTests
{
    [Fact]
    public async Task SendAsync_ForASelfHandledEndpoint_RunsItWithNoHandlerStubbed()
    {
        // Arrange: no options.Handle<…> call, because there is no message to stub.
        using var harness = EndpointHarness.Create<ProbeEndpoint>();

        // Act
        var response = await harness.Get("/health/live").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var body = response.ReadJson<ProbeDto>();
        Assert.NotNull(body);
        Assert.Equal("live", body!.Probe);
    }

    [Fact]
    public async Task SendAsync_WhenASelfHandledEndpointFails_MapsItThroughTheRealFailureMapper()
    {
        // Arrange
        using var harness = EndpointHarness.Create<MissingProbeEndpoint>();

        // Act
        var response = await harness.Get("/health-missing").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
    }

    internal sealed record ProbeQuery(string Probe);

    internal sealed record ProbeDto(string Probe);

    [Get("/health/{probe}")]
    internal sealed partial class ProbeEndpoint : SelfHandledEndpoint<ProbeQuery, ProbeDto>
    {
        public override ValueTask<Result<ProbeDto>> ExecuteAsync(ProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe)));
        }
    }

    // A constructor default, so the generated binding succeeds with no request input: this test's
    // subject is the failure mapping, not the binding.
    internal sealed record MissingProbeQuery(string Probe = "live");

    [Get("/health-missing")]
    internal sealed partial class MissingProbeEndpoint : SelfHandledEndpoint<MissingProbeQuery, ProbeDto>
    {
        public override ValueTask<Result<ProbeDto>> ExecuteAsync(MissingProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Failure<ProbeDto>(new NotFoundFailure("Probe", request.Probe)));
        }
    }
}
