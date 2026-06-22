using Microsoft.Extensions.Options;
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

        var options = Options.Create(new InvokerOptions());
        options.Value.RequestDispatchers[typeof(RequestWithResponseExample)] =
            (Func<IRequest<int>, IDependencyResolver, CancellationToken, ValueTask<Result<int>>>)(
                (request, resolver, ct) =>
                {
                    var handler = resolver.GetRequiredService<IRequestHandler<RequestWithResponseExample, int>>();
                    return handler.HandleAsync((RequestWithResponseExample)request, ct);
                });
        options.Value.VoidRequestDispatchers[typeof(RequestExample)] =
            (Func<IRequest, IDependencyResolver, CancellationToken, ValueTask<Result>>)(
                (request, resolver, ct) =>
                {
                    var handler = resolver.GetRequiredService<IRequestHandler<RequestExample>>();
                    return handler.HandleAsync((RequestExample)request, ct);
                });

        _invoker = new Invoker(_resolver, options);
    }

    [Fact]
    public async Task GivenAValidHandlerWithResponse_WhenHandleAsync_ShouldReturnAResponse()
    {
        // Arrange (Given)
        var request = new RequestWithResponseExample();
        var handler = Substitute.For<IRequestHandler<RequestWithResponseExample, int>>();

        _resolver.GetRequiredService<IRequestHandler<RequestWithResponseExample, int>>()
            .Returns(handler);
        handler.HandleAsync(request, CancellationToken.None)
            .Returns(Result.Success(42));

        // Act (When)
        var result = await _invoker.InvokeAsync(request, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
        if (result.TryGet(out var value, out _))
        {
            Assert.Equal(42, value);
        }
        else
        {
            Assert.Fail("Result should be successful but was marked as failed");
        }
    }

    [Fact]
    public async Task GivenAValidHandlerWithoutResponse_WhenHandleAsync_ShouldReturnAResponse()
    {
        // Arrange (Given)
        var request = new RequestExample();
        var handler = Substitute.For<IRequestHandler<RequestExample>>();

        _resolver.GetRequiredService<IRequestHandler<RequestExample>>()
            .Returns(handler);
        handler.HandleAsync(request, CancellationToken.None)
            .Returns(Result.Success());

        // Act (When)
        var result = await _invoker.InvokeAsync(request, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GivenARequestHeldByItsInterfaceStaticType_WhenInvokeWithResponse_ResolvesByRuntimeType()
    {
        // Arrange (Given)
        IRequest<int> request = new RequestWithResponseExample();
        var handler = Substitute.For<IRequestHandler<RequestWithResponseExample, int>>();

        _resolver.GetRequiredService<IRequestHandler<RequestWithResponseExample, int>>()
            .Returns(handler);
        handler.HandleAsync((RequestWithResponseExample)request, CancellationToken.None)
            .Returns(Result.Success(42));

        // Act (When)
        var result = await _invoker.InvokeAsync(request, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GivenARequestHeldByItsInterfaceStaticType_WhenInvokeWithoutResponse_ResolvesByRuntimeType()
    {
        // Arrange (Given)
        IRequest request = new RequestExample();
        var handler = Substitute.For<IRequestHandler<RequestExample>>();

        _resolver.GetRequiredService<IRequestHandler<RequestExample>>()
            .Returns(handler);
        handler.HandleAsync((RequestExample)request, CancellationToken.None)
            .Returns(Result.Success());

        // Act (When)
        var result = await _invoker.InvokeAsync(request, CancellationToken.None);

        // Assert (Then)
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GivenAnUnregisteredRequestWithoutResponse_WhenInvoke_ThrowsFriendlyError()
    {
        // Arrange (Given)
        var request = new UnregisteredRequestExample();

        // Act (When)
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _invoker.InvokeAsync(request, CancellationToken.None));

        // Assert (Then)
        Assert.Contains("No handler registered for request type", exception.Message);
    }

    private sealed record UnregisteredRequestExample : IRequest;
}