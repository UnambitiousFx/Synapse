using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed class RequestBuildingTests
{
    [Fact]
    public async Task Query_WhenSet_IsVisibleToTheEndpoint()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<EchoInputEndpoint>(new EndpointMetadata(["GET"], "/echo"));
        using var harness = EndpointHarness.Create<EchoInputEndpoint>();

        // Act
        var response = await harness.Get("/echo").Query("search", "widgets").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("widgets", response.ReadJson<Echo>()!.Search);
    }

    [Fact]
    public async Task Get_WhenTheUrlCarriesItsOwnQueryString_IsVisibleToTheEndpoint()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<EchoInputEndpoint>(new EndpointMetadata(["GET"], "/echo"));
        using var harness = EndpointHarness.Create<EchoInputEndpoint>();

        // Act
        var response = await harness.Get("/echo?search=gadgets").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("gadgets", response.ReadJson<Echo>()!.Search);
    }

    [Fact]
    public async Task Header_WhenSet_IsVisibleToTheEndpoint()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<EchoInputEndpoint>(new EndpointMetadata(["GET"], "/echo"));
        using var harness = EndpointHarness.Create<EchoInputEndpoint>();

        // Act
        var response = await harness.Get("/echo").Header("X-Tenant", "acme").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("acme", response.ReadJson<Echo>()!.Tenant);
    }

    [Fact]
    public async Task JsonBody_WhenSet_IsReadableAsJsonByTheEndpoint()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<EchoBodyEndpoint>(new EndpointMetadata(["POST"], "/echo-body"));
        using var harness = EndpointHarness.Create<EchoBodyEndpoint>();

        // Act
        var response = await harness.Post("/echo-body")
            .JsonBody(new Echo { Search = "posted", Tenant = "acme" })
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("posted", response.ReadJson<Echo>()!.Search);
    }

    [Fact]
    public async Task Body_WhenSet_IsReadableVerbatimByTheEndpoint()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<EchoRawBodyEndpoint>(new EndpointMetadata(["POST"], "/echo-raw"));
        using var harness = EndpointHarness.Create<EchoRawBodyEndpoint>();

        // Act
        var response = await harness.Post("/echo-raw").Body("<xml/>", "application/xml").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("application/xml|<xml/>", response.Body);
    }

    private sealed class Echo
    {
        public string? Search { get; set; }

        public string? Tenant { get; set; }
    }

    private sealed class EchoInputEndpoint : RawEndpoint
    {
        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IResult>(TypedResults.Ok(new Echo
            {
                Search = context.Request.Query["search"],
                Tenant = context.Request.Headers["X-Tenant"]
            }));
        }
    }

    private sealed class EchoBodyEndpoint : RawEndpoint
    {
        public override async ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            var echo = await context.Request.ReadFromJsonAsync<Echo>(cancellationToken);
            return TypedResults.Ok(echo);
        }
    }

    private sealed class EchoRawBodyEndpoint : RawEndpoint
    {
        public override async ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);
            return TypedResults.Text($"{context.Request.ContentType}|{body}");
        }
    }
}
