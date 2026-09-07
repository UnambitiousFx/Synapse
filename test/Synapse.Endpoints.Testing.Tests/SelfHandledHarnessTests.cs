using Microsoft.AspNetCore.Http;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed partial class SelfHandledHarnessTests
{
    [Fact]
    public async Task SendAsync_ForASelfHandledEndpoint_RunsItWithNoHandlerStubbed()
    {
        // Arrange: no options.Handle<…> call, because there is no message to stub.
        EndpointRegistry.RegisterBinder(new ProbeBinder());
        EndpointRegistry.RegisterMetadata<ProbeEndpoint>(new EndpointMetadata(["GET"], "/health/{probe}"));
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
        EndpointRegistry.RegisterBinder(new MissingProbeBinder());
        EndpointRegistry.RegisterMetadata<MissingProbeEndpoint>(new EndpointMetadata(["GET"], "/health-missing"));
        using var harness = EndpointHarness.Create<MissingProbeEndpoint>();

        // Act
        var response = await harness.Get("/health-missing").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
    }

    private sealed record ProbeQuery(string Probe);

    private sealed record ProbeDto(string Probe);

    private sealed partial class ProbeEndpoint : SelfHandledEndpoint<ProbeQuery, ProbeDto>
    {
        public override ValueTask<Result<ProbeDto>> ExecuteAsync(ProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe)));
        }
    }

    private sealed class ProbeBinder : IEndpointBinder<ProbeQuery>
    {
        public ValueTask<BindResult<ProbeQuery>> BindAsync(HttpContext context)
        {
            return context.TryGetRoute("probe", out var probe)
                ? ValueTask.FromResult(BindResult<ProbeQuery>.Success(new ProbeQuery(probe!)))
                : ValueTask.FromResult(BindResult<ProbeQuery>.Failure("probe", "is required."));
        }
    }

    private sealed record MissingProbeQuery(string Probe);

    private sealed partial class MissingProbeEndpoint : SelfHandledEndpoint<MissingProbeQuery, ProbeDto>
    {
        public override ValueTask<Result<ProbeDto>> ExecuteAsync(MissingProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Failure<ProbeDto>(new NotFoundFailure("Probe", request.Probe)));
        }
    }

    private sealed class MissingProbeBinder : IEndpointBinder<MissingProbeQuery>
    {
        public ValueTask<BindResult<MissingProbeQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<MissingProbeQuery>.Success(new MissingProbeQuery("live")));
        }
    }
}
