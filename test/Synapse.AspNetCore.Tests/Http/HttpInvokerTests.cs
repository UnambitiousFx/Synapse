using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;

namespace UnambitiousFx.Synapse.AspNetCore.Tests.Http;

public sealed class HttpInvokerTests
{
    [Fact]
    public async Task InvokeAsync_WithOnSuccess_WhenRequestSucceeds_ReturnsOnSuccessResult()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var failureMapper = Substitute.For<IFailureHttpMapper>();
        var sut = new HttpInvoker(invoker, failureMapper, Options.Create(new SynapseAspNetCoreOptions()));
        var request = new IntRequest();
        var expectedResult = new StaticResult(299);

        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(10)));

        // Act (When)
        var result = await sut.InvokeAsync(request, _ => expectedResult, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Same(expectedResult, result);
    }

    [Fact]
    public async Task InvokeAsync_WithoutOnSuccess_WhenRequestSucceeds_ReturnsHttpResult()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var failureMapper = Substitute.For<IFailureHttpMapper>();
        var sut = new HttpInvoker(invoker, failureMapper, Options.Create(new SynapseAspNetCoreOptions()));
        var request = new IntRequest();

        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(42)));

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotNull(result);
    }

    [Fact]
    public async Task InvokeAsync_VoidRequest_WhenRequestSucceeds_ReturnsHttpResult()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var failureMapper = Substitute.For<IFailureHttpMapper>();
        var sut = new HttpInvoker(invoker, failureMapper, Options.Create(new SynapseAspNetCoreOptions()));
        var request = new VoidRequest();

        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotNull(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenUnauthenticated_Returns401WithBearerChallenge()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var sut = new HttpInvoker(invoker, new DefaultFailureHttpMapper(), Options.Create(new SynapseAspNetCoreOptions()));
        var request = new IntRequest();
        var http = new HttpTestContext();

        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Failure<int>(new UnauthenticatedFailure("Authentication is required."))));

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);
        await result.ExecuteAsync(http.Context);
        await http.StartAsync();

        // Assert (Then)
        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.Equal("Bearer", http.Response.Headers.WWWAuthenticate.ToString());
    }

    [Theory]
    [InlineData("Basic realm=\"api\"", "Basic realm=\"api\"")]
    [InlineData(null, null)]
    public async Task InvokeAsync_WhenUnauthenticated_WritesConfiguredChallenge(string? configured, string? expected)
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var options = Options.Create(new SynapseAspNetCoreOptions { AuthenticationChallenge = configured });
        var sut = new HttpInvoker(invoker, new DefaultFailureHttpMapper(), options);
        var request = new VoidRequest();
        var http = new HttpTestContext();

        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Failure(new UnauthenticatedFailure("Authentication is required."))));

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);
        await result.ExecuteAsync(http.Context);
        await http.StartAsync();

        // Assert (Then)
        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.Equal(expected, http.Response.Headers.WWWAuthenticate.FirstOrDefault());
    }

    [Fact]
    public async Task InvokeStreamAsync_WhenResultNeverExecutes_DoesNotOpenStream()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var sut = new HttpInvoker(invoker, new DefaultFailureHttpMapper(), Options.Create(new SynapseAspNetCoreOptions()));
        var request = new IntStreamRequest();

        // Act (When)
        _ = await sut.InvokeStreamAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        invoker.DidNotReceive().InvokeStreamAsync(Arg.Any<IStreamRequest<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_WhenForbidden_Returns403WithoutChallenge()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var sut = new HttpInvoker(invoker, new DefaultFailureHttpMapper(), Options.Create(new SynapseAspNetCoreOptions()));
        var request = new IntRequest();
        var http = new HttpTestContext();

        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Failure<int>(new UnauthorizedFailure("Denied."))));

        // Act (When)
        var result = await sut.InvokeAsync(request, _ => new StaticResult(200), TestContext.Current.CancellationToken);
        await result.ExecuteAsync(http.Context);
        await http.StartAsync();

        // Assert (Then)
        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        Assert.False(http.Response.Headers.ContainsKey("WWW-Authenticate"));
    }

    [Fact]
    public async Task InvokeStreamAsync_WhenFirstItemIsDenied_ReturnsMappedFailure()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var sut = new HttpInvoker(invoker, new DefaultFailureHttpMapper(), Options.Create(new SynapseAspNetCoreOptions()));
        var request = new IntStreamRequest();
        var http = new HttpTestContext();

        invoker.InvokeStreamAsync(request, Arg.Any<CancellationToken>())
            .Returns(Stream(Result.Failure<int>(new UnauthenticatedFailure("Authentication is required."))));

        // Act (When)
        var result = await sut.InvokeStreamAsync(request, TestContext.Current.CancellationToken);
        await result.ExecuteAsync(http.Context);
        await http.StartAsync();

        // Assert (Then)
        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.Equal("Bearer", http.Response.Headers.WWWAuthenticate.ToString());
        Assert.Equal("application/problem+json", http.Response.ContentType);
    }

    [Fact]
    public async Task InvokeStreamAsync_WhenAllItemsSucceed_Streams200JsonArray()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var sut = new HttpInvoker(invoker, new DefaultFailureHttpMapper(), Options.Create(new SynapseAspNetCoreOptions()));
        var request = new IntStreamRequest();
        var http = new HttpTestContext();

        invoker.InvokeStreamAsync(request, Arg.Any<CancellationToken>())
            .Returns(Stream(Result.Success(1), Result.Success(2)));

        // Act (When)
        var result = await sut.InvokeStreamAsync(request, TestContext.Current.CancellationToken);
        await result.ExecuteAsync(http.Context);

        // Assert (Then)
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
        Assert.Equal("[1,2]", http.Body());
    }

    [Fact]
    public async Task InvokeStreamAsync_WhenStreamIsEmpty_Returns200EmptyArray()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var sut = new HttpInvoker(invoker, new DefaultFailureHttpMapper(), Options.Create(new SynapseAspNetCoreOptions()));
        var request = new IntStreamRequest();
        var http = new HttpTestContext();

        invoker.InvokeStreamAsync(request, Arg.Any<CancellationToken>())
            .Returns(Stream());

        // Act (When)
        var result = await sut.InvokeStreamAsync(request, TestContext.Current.CancellationToken);
        await result.ExecuteAsync(http.Context);

        // Assert (Then)
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
        Assert.Equal("[]", http.Body());
    }

    [Fact]
    public async Task InvokeStreamAsync_WhenItemFailsAfterResponseStarted_Throws()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var sut = new HttpInvoker(invoker, new DefaultFailureHttpMapper(), Options.Create(new SynapseAspNetCoreOptions()));
        var request = new IntStreamRequest();
        var http = new HttpTestContext();

        invoker.InvokeStreamAsync(request, Arg.Any<CancellationToken>())
            .Returns(Stream(Result.Success(1), Result.Failure<int>("failed"), Result.Success(3)));

        // Act (When)
        var result = await sut.InvokeStreamAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        await Assert.ThrowsAsync<InvalidOperationException>(() => result.ExecuteAsync(http.Context));
    }

    private static async IAsyncEnumerable<Result<int>> Stream(params Result<int>[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private sealed record IntRequest : IRequest<int>;

    private sealed record VoidRequest : IRequest;

    private sealed record IntStreamRequest : IStreamRequest<int>;

    private sealed class StaticResult(int statusCode) : Microsoft.AspNetCore.Http.IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }
    }
}
