using Microsoft.AspNetCore.Http;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed class BinderTierHarnessTests
{
    [Fact]
    public async Task SendAsync_ForAnEndpointWithAResponse_BindsDispatchesAndMaps()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new LookupBinder());
        EndpointRegistry.RegisterMetadata<LookupEndpoint>(new EndpointMetadata(["GET"], "/lookup/{id}"));
        using var harness = EndpointHarness.Create<LookupEndpoint>(options =>
            options.Handle<LookupQuery, string>(query => Result.Success($"found:{query.Id}")));

        // Act
        var response = await harness.Get("/lookup/42").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("found:42", response.ReadJson<string>());
    }

    [Fact]
    public async Task SendAsync_ForAVoidEndpoint_DispatchesAndReturnsNoContent()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new RetireBinder());
        EndpointRegistry.RegisterMetadata<RetireEndpoint>(new EndpointMetadata(["DELETE"], "/retire"));
        using var harness = EndpointHarness.Create<RetireEndpoint>(options =>
            options.Handle<RetireCommand>(_ => Result.Success()));

        // Act
        var response = await harness.Delete("/retire").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status204NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WhenBindingFails_Returns400WithTheFieldThatFailed()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new RejectingBinder());
        EndpointRegistry.RegisterMetadata<RejectingEndpoint>(new EndpointMetadata(["GET"], "/rejecting"));
        using var harness = EndpointHarness.Create<RejectingEndpoint>();

        // Act
        var response = await harness.Get("/rejecting").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        var problem = response.ReadValidationProblem();
        Assert.Equal("The route value is not a valid Guid.", Assert.Single(problem.Errors["taskId"]));
    }

    [Fact]
    public async Task SendAsync_ForAMappedEndpoint_MapsBothWays()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new WireBinder());
        EndpointRegistry.RegisterMetadata<TranslateEndpoint>(new EndpointMetadata(["POST"], "/translate"));
        using var harness = EndpointHarness.Create<TranslateEndpoint>(options =>
            options.Handle<TranslateCommand, TranslateResult>(
                command => Result.Success(new TranslateResult(command.Text.ToUpperInvariant()))));

        // Act
        var response = await harness.Post("/translate").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("HELLO!", response.ReadJson<WireResponse>()!.Value);
    }

    private sealed record LookupQuery : IRequest<string>
    {
        public string Id { get; init; } = string.Empty;
    }

    private sealed class LookupEndpoint : Endpoint<LookupQuery, string>;

    private sealed class LookupBinder : IEndpointBinder<LookupQuery>
    {
        public ValueTask<BindResult<LookupQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<LookupQuery>.Success(
                new LookupQuery { Id = context.Request.RouteValues["id"]?.ToString() ?? string.Empty }));
        }
    }

    private sealed record RetireCommand : IRequest;

    private sealed class RetireEndpoint : Endpoint<RetireCommand>;

    private sealed class RetireBinder : IEndpointBinder<RetireCommand>
    {
        public ValueTask<BindResult<RetireCommand>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<RetireCommand>.Success(new RetireCommand()));
        }
    }

    private sealed record RejectingQuery : IRequest<string>;

    private sealed class RejectingEndpoint : Endpoint<RejectingQuery, string>;

    private sealed class RejectingBinder : IEndpointBinder<RejectingQuery>
    {
        public ValueTask<BindResult<RejectingQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(
                BindResult<RejectingQuery>.Failure("taskId", "The route value is not a valid Guid."));
        }
    }

    private sealed record WireRequest(string Text);

    private sealed record WireResponse(string Value);

    private sealed record TranslateCommand(string Text) : IRequest<TranslateResult>;

    private sealed record TranslateResult(string Text);

    private sealed class TranslateEndpoint
        : MappedEndpoint<WireRequest, TranslateCommand, TranslateResult, WireResponse>
    {
        public override TranslateCommand ToRequest(WireRequest request)
        {
            return new TranslateCommand(request.Text);
        }

        public override WireResponse ToResponse(TranslateResult response)
        {
            return new WireResponse(response.Text);
        }
    }

    private sealed class WireBinder : IEndpointBinder<WireRequest>
    {
        public ValueTask<BindResult<WireRequest>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<WireRequest>.Success(new WireRequest("hello!")));
        }
    }
}
