using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed partial class RequestBuildingTests
{
    [Fact]
    public async Task Query_WhenSet_IsVisibleToTheEndpoint()
    {
        // Arrange
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
        using var harness = EndpointHarness.Create<EchoRawBodyEndpoint>();

        // Act
        var response = await harness.Post("/echo-raw").Body("<xml/>", "application/xml").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("application/xml|<xml/>", response.Body);
    }

    [Fact]
    public async Task FormBody_WhenAValueContainsReservedCharacters_ProducesTheExactUrlEncodedBody()
    {
        // Arrange — every FormBindingHarnessTests case uses "hello", a value needing no encoding at
        // all, so a broken encoder would pass every one of them. This reads the raw request instead
        // of going through a form binder: the point is to test the builder, not the parser.
        using var harness = EndpointHarness.Create<EchoRawBodyEndpoint>();

        // Act
        var response = await harness.Post("/echo-raw")
            .FormBody(("q", "a&b=c+d e"))
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("application/x-www-form-urlencoded|q=a%26b%3Dc%2Bd+e", response.Body);
    }

    [Fact]
    public async Task MultipartBody_WhenAFileNameNeedsEscaping_ProducesAnEscapedContentDisposition()
    {
        // Arrange — an unescaped quote in "filename" terminates the Content-Disposition quoted
        // string early, which corrupts the header rather than merely the file name. This reads the
        // raw request instead of going through a form binder: the point is to test the builder, not
        // the parser.
        using var harness = EndpointHarness.Create<EchoRawBodyEndpoint>();

        // MultipartBody's boundary is a fixed, undocumented implementation detail, duplicated here
        // only to spell out the expected wire bytes precisely.
        const string boundary = "------------------------synapse";
        var expectedBody =
            $"--{boundary}\r\n" +
            "Content-Disposition: form-data; name=\"file\"; filename=\"my\\\"file.txt\"\r\n" +
            "Content-Type: application/octet-stream\r\n\r\n" +
            "hi\r\n" +
            $"--{boundary}--\r\n";

        // Act
        var response = await harness.Post("/echo-raw")
            .MultipartBody([], [("file", "my\"file.txt", "hi")])
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"multipart/form-data; boundary={boundary}|{expectedBody}", response.Body);
    }

    [Fact]
    public async Task ReadJson_WhenTheHarnessConfiguresACustomNamingPolicy_ReadsWithThoseSameOptions()
    {
        // Arrange: the endpoint serializes SearchTerm as "search_term" under this policy. A reader
        // using different options (a hardcoded, default-cased JsonSerializerOptions, say) would not
        // throw - System.Text.Json just leaves the unmatched property at its default, so the
        // assertion below would see null instead of "widgets".
        using var harness = EndpointHarness.Create<EchoSnakeCaseEndpoint>(options =>
            options.Services.ConfigureHttpJsonOptions(json =>
                json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower));

        // Act
        var response = await harness.Get("/echo-snake").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("widgets", response.ReadJson<SnakeEcho>()!.SearchTerm);
    }

    private sealed class Echo
    {
        public string? Search { get; set; }

        public string? Tenant { get; set; }
    }

    private sealed class SnakeEcho
    {
        public string? SearchTerm { get; set; }
    }

    [Get("/echo")]
    internal sealed partial class EchoInputEndpoint : RawEndpoint
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

    [Get("/echo-snake")]
    internal sealed partial class EchoSnakeCaseEndpoint : RawEndpoint
    {
        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IResult>(TypedResults.Ok(new SnakeEcho { SearchTerm = "widgets" }));
        }
    }

    [Post("/echo-body")]
    internal sealed partial class EchoBodyEndpoint : RawEndpoint
    {
        public override async ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            var echo = await context.Request.ReadFromJsonAsync<Echo>(cancellationToken);
            return TypedResults.Ok(echo);
        }
    }

    [Post("/echo-raw")]
    internal sealed partial class EchoRawBodyEndpoint : RawEndpoint
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
