using Microsoft.AspNetCore.Http;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.AspNetCore.Mappers;
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
        var sut = new HttpInvoker(invoker, failureMapper);
        var request = new IntRequest();
        var expectedResult = new StaticResult(299);

        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(10)));

        // Act (When)
        var result = await sut.InvokeAsync(request, _ => expectedResult, CancellationToken.None);

        // Assert (Then)
        Assert.Same(expectedResult, result);
    }

    [Fact]
    public async Task InvokeAsync_WithoutOnSuccess_WhenRequestSucceeds_ReturnsHttpResult()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var failureMapper = Substitute.For<IFailureHttpMapper>();
        var sut = new HttpInvoker(invoker, failureMapper);
        var request = new IntRequest();

        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success(42)));

        // Act (When)
        var result = await sut.InvokeAsync(request, CancellationToken.None);

        // Assert (Then)
        Assert.NotNull(result);
    }

    [Fact]
    public async Task InvokeAsync_VoidRequest_WhenRequestSucceeds_ReturnsHttpResult()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var failureMapper = Substitute.For<IFailureHttpMapper>();
        var sut = new HttpInvoker(invoker, failureMapper);
        var request = new VoidRequest();

        invoker.InvokeAsync(request, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        // Act (When)
        var result = await sut.InvokeAsync(request, CancellationToken.None);

        // Assert (Then)
        Assert.NotNull(result);
    }

    [Fact]
    public async Task InvokeStreamAsync_WhenStreamContainsFailures_FiltersFailedItems()
    {
        // Arrange (Given)
        var invoker = Substitute.For<IInvoker>();
        var failureMapper = Substitute.For<IFailureHttpMapper>();
        var sut = new HttpInvoker(invoker, failureMapper);
        var request = new IntStreamRequest();

        invoker.InvokeStreamAsync(request, Arg.Any<CancellationToken>())
            .Returns(CreateStream());

        // Act (When)
        var items = new List<int>();
        await foreach (var item in sut.InvokeStreamAsync(request, CancellationToken.None))
        {
            items.Add(item);
        }

        // Assert (Then)
        Assert.Equal([1, 3], items);
    }

    private static async IAsyncEnumerable<Result<int>> CreateStream()
    {
        yield return Result.Success(1);
        yield return Result.Failure<int>("failed");
        yield return Result.Success(3);
        await Task.CompletedTask;
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
