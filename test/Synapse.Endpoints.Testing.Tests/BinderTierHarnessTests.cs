using Microsoft.AspNetCore.Http;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed partial class BinderTierHarnessTests
{
    [Fact]
    public async Task SendAsync_ForAnEndpointWithAResponse_BindsDispatchesAndMaps()
    {
        // Arrange
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
        // Arrange — a real bad value rather than a stubbed failure: the binding is generated now, so
        // the only way to make it fail is to send something it cannot parse.
        EndpointRegistry.RegisterMetadata<RejectingEndpoint>(
            new EndpointMetadata(["GET"], "/rejecting/{taskId}"));
        using var harness = EndpointHarness.Create<RejectingEndpoint>();

        // Act
        var response = await harness.Get("/rejecting/not-a-guid").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        var problem = response.ReadValidationProblem();
        Assert.Equal("The route value is not a valid System.Guid.", Assert.Single(problem.Errors["taskId"]));
    }

    [Fact]
    public async Task SendAsync_ForAMappedEndpoint_MapsBothWays()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<TranslateEndpoint>(
            new EndpointMetadata(["POST"], "/translate/{text}"));
        using var harness = EndpointHarness.Create<TranslateEndpoint>(options =>
            options.Handle<TranslateCommand, TranslateResult>(
                command => Result.Success(new TranslateResult(command.Text.ToUpperInvariant()))));

        // Act
        var response = await harness.Post("/translate/hello!").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("HELLO!", response.ReadJson<WireResponse>()!.Value);
    }

    internal sealed record LookupQuery : IRequest<string>
    {
        public string Id { get; init; } = string.Empty;
    }

    [Get("/lookup/{id}")]
    internal sealed partial class LookupEndpoint : Endpoint<LookupQuery, string>;

    internal sealed record RetireCommand : IRequest;

    [Delete("/retire")]
    internal sealed partial class RetireEndpoint : Endpoint<RetireCommand>;

    internal sealed record RejectingQuery : IRequest<string>
    {
        // A Guid route parameter is how a generated binding is made to fail: send a segment that is
        // not one.
        public Guid TaskId { get; init; }
    }

    [Get("/rejecting/{taskId}")]
    internal sealed partial class RejectingEndpoint : Endpoint<RejectingQuery, string>;

    internal sealed record WireRequest(string Text);

    internal sealed record WireResponse(string Value);

    internal sealed record TranslateCommand(string Text) : IRequest<TranslateResult>;

    internal sealed record TranslateResult(string Text);

    // The route carries the wire DTO's Text so the generated binding reads it from there rather than
    // from a JSON body this test does not send.
    [Post("/translate/{text}")]
    internal sealed partial class TranslateEndpoint
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
}
