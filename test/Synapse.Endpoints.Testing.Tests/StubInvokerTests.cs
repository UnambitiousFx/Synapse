using Microsoft.AspNetCore.Http;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed partial class StubInvokerTests
{
    [Fact]
    public async Task SendAsync_WhenTheStubSucceeds_ReturnsTheMappedSuccess()
    {
        // Arrange
        using var harness = EndpointHarness.Create<GreetEndpoint>(options =>
            options.Handle<GreetQuery, string>(_ => Result.Success("hello")));

        // Act
        var response = await harness.Get("/greet").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("hello", response.ReadJson<string>());
    }

    [Fact]
    public async Task SendAsync_WhenTheStubFailsNotFound_ReturnsTheRealFailureMappersStatus()
    {
        // Arrange: the point of stubbing IInvoker rather than IHttpInvoker - the 404 below is
        // written by the real DefaultFailureHttpMapper, not by the harness.
        using var harness = EndpointHarness.Create<GreetEndpoint>(options =>
            options.Handle<GreetQuery, string>(_ => Result.FailNotFound<string>("Greeting", "1")));

        // Act
        var response = await harness.Get("/greet").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_ForAVoidCommand_ReturnsNoContent()
    {
        // Arrange
        using var harness = EndpointHarness.Create<PurgeEndpoint>(options =>
            options.Handle<PurgeCommand>(_ => Result.Success()));

        // Act
        var response = await harness.Delete("/purge").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status204NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WhenTheStubIsAsynchronous_IsAwaited()
    {
        // Arrange
        using var harness = EndpointHarness.Create<GreetEndpoint>(options =>
            options.Handle<GreetQuery, string>(async (_, token) =>
            {
                await Task.Delay(1, token);
                return Result.Success("awaited");
            }));

        // Act
        var response = await harness.Get("/greet").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("awaited", response.ReadJson<string>());
    }

    [Fact]
    public async Task SendAsync_WhenNoHandlerIsStubbed_ThrowsNamingTheHandleCallThatFixesIt()
    {
        // Arrange
        using var harness = EndpointHarness.Create<GreetEndpoint>();

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Get("/greet").SendAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("No handler is stubbed for 'GreetQuery'", exception.Message);
        Assert.Contains("options.Handle<GreetQuery, String>", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WhenTheStubbedResponseTypeIsWrong_ThrowsNamingTheExpectedType()
    {
        // Arrange: GreetQuery is dispatched as IRequest<string>, but the stub returns Result<int>.
        using var harness = EndpointHarness.Create<GreetEndpoint>(options =>
            options.Handle<GreetQuery, int>(_ => Result.Success(1)));

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Get("/greet").SendAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("does not return Result<String>", exception.Message);
    }

    internal sealed record GreetQuery : IRequest<string>, IRequest<int>;

    internal sealed record PurgeCommand : IRequest;

    [Get("/greet")]
    internal sealed partial class GreetEndpoint : RawEndpoint<GreetQuery, string>
    {
        public override ValueTask<BindResult<GreetQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<GreetQuery>.Success(new GreetQuery()));
        }
    }

    [Delete("/purge")]
    internal sealed partial class PurgeEndpoint : RawEndpoint<PurgeCommand>
    {
        public override ValueTask<BindResult<PurgeCommand>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<PurgeCommand>.Success(new PurgeCommand()));
        }
    }
}
