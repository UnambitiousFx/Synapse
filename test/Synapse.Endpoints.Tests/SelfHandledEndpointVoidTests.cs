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
        EndpointRegistry.RegisterBinder(new PurgeRequestBinder());
        EndpointRegistry.RegisterMetadata<PurgeEndpoint>(new EndpointMetadata(["DELETE"], "/cache/{key}"));

        var endpoint = new PurgeEndpoint();
        var context = Context();

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
        EndpointRegistry.RegisterBinder(new ConflictedRequestBinder());
        EndpointRegistry.RegisterMetadata<ConflictedEndpoint>(new EndpointMetadata(["DELETE"], "/cache-conflict"));

        var context = Context();

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
        EndpointRegistry.RegisterBinder(new QueuedRequestBinder());
        EndpointRegistry.RegisterMetadata<QueuedEndpoint>(new EndpointMetadata(["POST"], "/cache/rebuild"));

        var context = Context();

        // Act
        await ((EndpointBase)new QueuedEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<QueuedEndpoint>())
            .InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
    }

    private static DefaultHttpContext Context()
    {
        var services = new ServiceCollection();
        services.AddSynapseAspNetCore();
        services.AddLogging();

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
    }

    internal sealed record PurgeRequest(string Key);

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

    private sealed class PurgeRequestBinder : IEndpointBinder<PurgeRequest>
    {
        public ValueTask<BindResult<PurgeRequest>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<PurgeRequest>.Success(new PurgeRequest("stale")));
        }
    }

    internal sealed record ConflictedRequest(string Key);

    internal sealed partial class ConflictedEndpoint : SelfHandledEndpoint<ConflictedRequest>
    {
        public override ValueTask<Result> ExecuteAsync(ConflictedRequest request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Failure(new ConflictFailure("The cache is being rebuilt.")));
        }
    }

    private sealed class ConflictedRequestBinder : IEndpointBinder<ConflictedRequest>
    {
        public ValueTask<BindResult<ConflictedRequest>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<ConflictedRequest>.Success(new ConflictedRequest("stale")));
        }
    }

    internal sealed record QueuedRequest(string Key);

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

    private sealed class QueuedRequestBinder : IEndpointBinder<QueuedRequest>
    {
        public ValueTask<BindResult<QueuedRequest>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<QueuedRequest>.Success(new QueuedRequest("stale")));
        }
    }
}
