using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed class FormBindingHarnessTests
{
    [Fact]
    public async Task SendAsync_WhenAnEndpointDeclaresOnlyFormContentTypes_AnswersJsonWith415()
    {
        // Arrange — the whole design rests on a custom IAcceptsMetadata driving the consumes
        // matcher policy. A null RequestType is how we decline to describe a schema we defer;
        // this asserts that declining costs nothing at the matcher.
        EndpointRegistry.RegisterMetadata<ProbeEndpoint>(new EndpointMetadata(["POST"], "/probe"));
        using var harness = EndpointHarness.Create<ProbeEndpoint>();

        // Act
        var response = await harness.Post("/probe")
            .Body("{}", "application/json")
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WhenAnEndpointDeclaresFormContentTypes_AcceptsUrlEncoded()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<ProbeEndpoint>(new EndpointMetadata(["POST"], "/probe"));
        using var harness = EndpointHarness.Create<ProbeEndpoint>();

        // Act
        var response = await harness.Post("/probe")
            .Body("caption=hello", "application/x-www-form-urlencoded")
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
    }

    private sealed class ProbeAcceptsMetadata : IAcceptsMetadata
    {
        public Type? RequestType => null;

        public IReadOnlyList<string> ContentTypes { get; } =
            ["multipart/form-data", "application/x-www-form-urlencoded"];

        public bool IsOptional => false;
    }

    private sealed class ProbeEndpoint : RawEndpoint
    {
        public override void Configure(IRawEndpointBuilder builder)
        {
            builder.Raw(route => route.WithMetadata(new ProbeAcceptsMetadata()));
        }

        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<IResult>(TypedResults.Ok());
        }
    }
}
