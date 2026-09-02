using Microsoft.AspNetCore.Http;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed class StreamHarnessTests
{
    [Fact]
    public async Task SendAsync_ForAStreamEndpoint_MaterialisesTheStreamAsAJsonArray()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new TickerBinder());
        EndpointRegistry.RegisterMetadata<TickerEndpoint>(new EndpointMetadata(["GET"], "/ticks"));
        using var harness = EndpointHarness.Create<TickerEndpoint>(options =>
            options.HandleStream<TickerQuery, Tick>(_ => [new Tick(1), new Tick(2)]));

        // Act
        var response = await harness.Get("/ticks").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var ticks = response.ReadJson<Tick[]>()!;
        Assert.Equal([1, 2], ticks.Select(tick => tick.Value));
    }

    [Fact]
    public async Task SendAsync_WhenAcceptIsEventStream_MaterialisesTheStreamAsServerSentEvents()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new TickerBinder());
        EndpointRegistry.RegisterMetadata<TickerEndpoint>(new EndpointMetadata(["GET"], "/ticks"));
        using var harness = EndpointHarness.Create<TickerEndpoint>(options =>
            options.HandleStream<TickerQuery, Tick>(_ => [new Tick(7)]));

        // Act
        var response = await harness.Get("/ticks").Accept("text/event-stream")
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("text/event-stream", response.ContentType);
        Assert.Contains("data: {\"value\":7}", response.Body);
    }

    [Fact]
    public async Task SendAsync_WhenAnItemFails_SkipsItAndStreamsTheRest()
    {
        // Arrange: the skip is IHttpInvoker.InvokeStreamAsync's behaviour, and the harness keeps
        // that real - so the failing item disappears here exactly as it does on the wire.
        EndpointRegistry.RegisterBinder(new TickerBinder());
        EndpointRegistry.RegisterMetadata<TickerEndpoint>(new EndpointMetadata(["GET"], "/ticks"));
        using var harness = EndpointHarness.Create<TickerEndpoint>(options =>
            options.HandleStream<TickerQuery, Tick>(_ => Ticks()));

        // Act
        var response = await harness.Get("/ticks").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        var ticks = response.ReadJson<Tick[]>()!;
        Assert.Equal([1, 3], ticks.Select(tick => tick.Value));
        return;

        static async IAsyncEnumerable<Result<Tick>> Ticks()
        {
            yield return Result.Success(new Tick(1));
            yield return Result.Failure<Tick>("tick 2 is broken");
            yield return Result.Success(new Tick(3));
            await Task.CompletedTask;
        }
    }

    private sealed record Tick(int Value);

    private sealed record TickerQuery : IStreamRequest<Tick>;

    private sealed class TickerEndpoint : StreamEndpoint<TickerQuery, Tick>;

    private sealed class TickerBinder : IEndpointBinder<TickerQuery>
    {
        public ValueTask<BindResult<TickerQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<TickerQuery>.Success(new TickerQuery()));
        }
    }
}
