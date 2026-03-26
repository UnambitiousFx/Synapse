using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Resolvers;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Senders;

public sealed class InvokerTests
{
    private readonly Invoker _invoker;
    private readonly IDependencyResolver _resolver;

    public InvokerTests()
    {
        _resolver = Substitute.For<IDependencyResolver>();

        _invoker = new Invoker(_resolver);
    }

    [Fact]
    public async Task GivenAValidHandlerWithResponse_WhenHandleAsync_ShouldReturnAResponse()
    {
        // Arrange
        var request = new RequestWithResponseExample();
        var handler = Substitute.For<IRequestHandler<RequestWithResponseExample, int>>();

        _resolver.GetRequiredService<IRequestHandler<RequestWithResponseExample, int>>()
            .Returns(handler);
        handler.HandleAsync(request, CancellationToken.None)
            .Returns(Result.Success(42));

        // Act
        var result = await _invoker.InvokeAsync<RequestWithResponseExample, int>(request, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        if (result.TryGet(out int value))
            Assert.Equal(42, value);
        else
            Assert.Fail("Result should be successful but was marked as failed");
    }

    [Fact]
    public async Task GivenAValidHandlerWithoutResponse_WhenHandleAsync_ShouldReturnAResponse()
    {
        // Arrange
        var request = new RequestExample();
        var handler = Substitute.For<IRequestHandler<RequestExample>>();

        _resolver.GetRequiredService<IRequestHandler<RequestExample>>()
            .Returns(handler);
        handler.HandleAsync(request, CancellationToken.None)
            .Returns(Result.Success());

        // Act
        var result = await _invoker.InvokeAsync(request, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
    }
}