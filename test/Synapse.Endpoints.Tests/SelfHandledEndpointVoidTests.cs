using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class SelfHandledEndpointVoidTests
{
    [Fact]
    public async Task Invoke_WithNoResponse_RunsExecuteAsyncAndAnswers204()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<PurgeEndpoint>(new EndpointMetadata(["DELETE"], "/cache/{key}"));

        var endpoint = new PurgeEndpoint();
        var context = Context("stale");

        // Act
        await ((EndpointBase)endpoint)
            .CreateDescriptor(EndpointRegistry.GetMetadata<PurgeEndpoint>())
            .InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Equal("stale", endpoint.Purged);
    }

    [Fact]
    public async Task Invoke_WhenExecuteAsyncFails_MapsTheFailureThroughTheRegisteredMapper()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<ConflictedEndpoint>(
            new EndpointMetadata(["DELETE"], "/cache-conflict/{key}"));

        var context = Context("stale");

        // Act
        await ((EndpointBase)new ConflictedEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<ConflictedEndpoint>())
            .InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WithConfiguredStatusCode_UsesItInsteadOfOnSuccess()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<QueuedEndpoint>(
            new EndpointMetadata(["POST"], "/cache/rebuild/{key}"));

        var context = Context("stale");

        // Act
        await ((EndpointBase)new QueuedEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<QueuedEndpoint>())
            .InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
    }

    /// <summary>A request carrying <paramref name="key" /> as its <c>key</c> route value.</summary>
    /// <param name="key">The route value every endpoint here binds its <c>Key</c> from.</param>
    /// <returns>The context to invoke against.</returns>
    private static DefaultHttpContext Context(string key)
    {
        var services = new ServiceCollection();
        services.AddSynapseAspNetCore();
        services.AddLogging();

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
        context.Request.RouteValues["key"] = key;

        return context;
    }

    internal sealed record PurgeRequest(string Key);

    [Delete("/cache/{key}")]
    internal sealed partial class PurgeEndpoint : SelfHandledEndpoint<PurgeRequest>
    {
        public string? Purged { get; private set; }

        public override ValueTask<Result> ExecuteAsync(PurgeRequest request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            Purged = request.Key;
            return ValueTask.FromResult(Result.Success());
        }
    }

    internal sealed record ConflictedRequest(string Key);

    [Delete("/cache-conflict/{key}")]
    internal sealed partial class ConflictedEndpoint : SelfHandledEndpoint<ConflictedRequest>
    {
        public override ValueTask<Result> ExecuteAsync(ConflictedRequest request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Failure(new ConflictFailure("The cache is being rebuilt.")));
        }
    }

    internal sealed record QueuedRequest(string Key);

    [Post("/cache/rebuild/{key}")]
    internal sealed partial class QueuedEndpoint : SelfHandledEndpoint<QueuedRequest>
    {
        public override void Configure(IEndpointBuilder builder)
        {
            builder.StatusCode(StatusCodes.Status202Accepted);
        }

        public override ValueTask<Result> ExecuteAsync(QueuedRequest request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }
}
