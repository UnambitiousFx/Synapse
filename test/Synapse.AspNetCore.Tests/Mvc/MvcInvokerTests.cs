using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.AspNetCore.Mappers;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Mvc;

namespace UnambitiousFx.Synapse.AspNetCore.Tests.Mvc;

public sealed class MvcInvokerTests
{
    [Fact]
    public async Task InvokeAsync_WithOnSuccess_WhenRequestSucceeds_ReturnsOnSuccessActionResult()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var failureMapper = Substitute.For<IFailureHttpMapper>();
        var sut = new MvcInvoker(invoker, failureMapper);
        var request = new IntRequest();
        var expected = new AcceptedResult();

        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(22)));

        // Act (When)
        var result = await sut.InvokeAsync(request, _ => expected, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.Same(expected, result);
    }

    [Fact]
    public async Task InvokeAsync_WithoutOnSuccess_WhenRequestSucceeds_ReturnsActionResult()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var failureMapper = Substitute.For<IFailureHttpMapper>();
        var sut = new MvcInvoker(invoker, failureMapper);
        var request = new IntRequest();

        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(5)));

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);

        // Assert (Then)
        Assert.NotNull(result);
    }

    [Fact]
    public async Task InvokeAsync_VoidRequest_WhenRequestSucceeds_ReturnsActionResult()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var failureMapper = Substitute.For<IFailureHttpMapper>();
        var sut = new MvcInvoker(invoker, failureMapper);
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
        var sut = new MvcInvoker(invoker, new DefaultFailureHttpMapper());
        var request = new IntRequest();
        var http = new HttpTestContext(services => services.AddMvcCore());
        var actionContext = new ActionContext(http.Context, new RouteData(), new ActionDescriptor());

        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Failure<int>(new UnauthenticatedFailure("Authentication is required."))));

        // Act (When)
        var result = await sut.InvokeAsync(request, TestContext.Current.CancellationToken);
        await result.ExecuteResultAsync(actionContext);
        await http.StartAsync();

        // Assert (Then)
        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.Equal("Bearer", http.Response.Headers.WWWAuthenticate.ToString());
    }

    private sealed record IntRequest : IRequest<int>;

    private sealed record VoidRequest : IRequest;
}
