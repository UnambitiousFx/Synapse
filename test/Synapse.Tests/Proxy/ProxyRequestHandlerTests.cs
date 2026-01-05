using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Send;
using UnambitiousFx.Synapse.Tests.Definitions;

namespace UnambitiousFx.Synapse.Tests.Proxy;

public sealed class ProxyRequestHandlerTests
{
    [Fact]
    public async Task GivenARequestPipelineBehavior_WhenProxyHandle_ShouldCallTheBehavior()
    {
        var order = 0;
        Substitute.For<IPublisher>();
        var handler = new RequestWithResponseExampleHandler();
        handler.OnExecuted = () =>
        {
            Assert.Equal(1, order);
            order++;
        };
        var behavior = new TestRequestPipelineBehavior();
        behavior.OnExecuted = () =>
        {
            Assert.Equal(0, order);
            order++;
        };
        var proxy =
            new ProxyRequestHandler<RequestWithResponseExampleHandler, RequestWithResponseExample, int>(handler,
                [behavior]);
        var request = new RequestWithResponseExample();

        var result = await proxy.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(handler.Executed);
        Assert.Equal(1, handler.ExecutionCount);
        Assert.True(behavior.Executed);
        Assert.Equal(1, behavior.ExecutionCount);
    }
}