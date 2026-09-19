using Microsoft.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class EndpointProcessorsTests
{
    [Fact]
    public async Task RunPreAsync_WithNoProcessors_ReturnsNull()
    {
        // Arrange
        var processors = EndpointProcessors.Empty;

        // Act
        var result = await processors.RunPreAsync(new DefaultHttpContext(), CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RunPreAsync_WithProcessors_RunsThemInRegistrationOrder()
    {
        // Arrange
        var order = new List<string>();
        var processors = new EndpointProcessors(
            [_ => new RecordingPreProcessor("first", order, null),
             _ => new RecordingPreProcessor("second", order, null)],
            []);

        // Act
        var result = await processors.RunPreAsync(new DefaultHttpContext(), CancellationToken.None);

        // Assert
        Assert.Null(result);
        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public async Task RunPreAsync_WhenOneShortCircuits_StopsAndReturnsThatResult()
    {
        // Arrange
        var order = new List<string>();
        var stop = Results.StatusCode(409);
        var processors = new EndpointProcessors(
            [_ => new RecordingPreProcessor("first", order, stop),
             _ => new RecordingPreProcessor("second", order, null)],
            []);

        // Act
        var result = await processors.RunPreAsync(new DefaultHttpContext(), CancellationToken.None);

        // Assert
        Assert.Same(stop, result);
        Assert.Equal(["first"], order);
    }

    [Fact]
    public async Task RunPostAsync_WithNoProcessors_ReturnsTheResultUnchanged()
    {
        // Arrange
        var original = Results.NoContent();

        // Act
        var result = await EndpointProcessors.Empty.RunPostAsync(
            original, new DefaultHttpContext(), CancellationToken.None);

        // Assert
        Assert.Same(original, result);
    }

    [Fact]
    public async Task RunPostAsync_WithProcessors_FoldsTheResultThroughEachInRegistrationOrder()
    {
        // Arrange
        var order = new List<string>();
        var replacement = Results.StatusCode(202);
        var processors = new EndpointProcessors(
            [],
            [_ => new RecordingPostProcessor("first", order, null),
             _ => new RecordingPostProcessor("second", order, replacement)]);

        // Act
        var result = await processors.RunPostAsync(
            Results.NoContent(), new DefaultHttpContext(), CancellationToken.None);

        // Assert
        Assert.Same(replacement, result);
        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public async Task RunPostAsync_WhenAProcessorReturnsNull_ThrowsNamingThatProcessor()
    {
        // Arrange: the endpoint-level guard would report the endpoint, which does not say which of
        // several registered processors dropped the result.
        var processors = new EndpointProcessors([], [_ => new NullReturningPostProcessor()]);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await processors.RunPostAsync(
                Results.NoContent(), new DefaultHttpContext(), CancellationToken.None));

        // Assert
        Assert.Contains(nameof(NullReturningPostProcessor), exception.Message);
    }

    private sealed class RecordingPreProcessor : IEndpointPreProcessor
    {
        private readonly string _name;
        private readonly List<string> _order;
        private readonly IResult? _shortCircuit;

        internal RecordingPreProcessor(string name, List<string> order, IResult? shortCircuit)
        {
            _name = name;
            _order = order;
            _shortCircuit = shortCircuit;
        }

        public ValueTask<IResult?> ProcessAsync(HttpContext context, CancellationToken cancellationToken)
        {
            _order.Add(_name);
            return new ValueTask<IResult?>(_shortCircuit);
        }
    }

    private sealed class RecordingPostProcessor : IEndpointPostProcessor
    {
        private readonly string _name;
        private readonly List<string> _order;
        private readonly IResult? _replacement;

        internal RecordingPostProcessor(string name, List<string> order, IResult? replacement)
        {
            _name = name;
            _order = order;
            _replacement = replacement;
        }

        public ValueTask<IResult> ProcessAsync(IResult result, HttpContext context,
            CancellationToken cancellationToken)
        {
            _order.Add(_name);
            return new ValueTask<IResult>(_replacement ?? result);
        }
    }

    private sealed class NullReturningPostProcessor : IEndpointPostProcessor
    {
        public ValueTask<IResult> ProcessAsync(IResult result, HttpContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<IResult>((IResult)null!);
        }
    }
}
