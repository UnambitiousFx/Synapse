using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class EndpointVoidTests
{
    [Fact]
    public async Task Invoke_WithDefaultConfiguration_Returns204()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<PingEndpoint>(new EndpointMetadata(["POST"], "/ping"));

        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<PingCommand>(), Arg.Any<Func<Microsoft.AspNetCore.Http.IResult>>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(call.Arg<Func<Microsoft.AspNetCore.Http.IResult>>()()));

        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var descriptor = ((EndpointBase)new PingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<PingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WhenInvokerReturnsMappedFailure_Returns409WithoutInvokingOnSuccess()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<ConflictPingEndpoint>(new EndpointMetadata(["POST"], "/ping-conflict"));

        // A real HttpInvoker never calls onSuccess for a mapped failure (see HttpInvokerTests); this
        // substitute mirrors that by returning the mapped failure directly, ignoring the factory.
        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<ConflictPingCommand>(), Arg.Any<Func<Microsoft.AspNetCore.Http.IResult>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Microsoft.AspNetCore.Http.IResult>(
                TypedResults.Problem(statusCode: StatusCodes.Status409Conflict)));

        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var descriptor = ((EndpointBase)new ConflictPingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<ConflictPingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WithConfiguredStatusCode_Returns202ThroughRealHttpInvoker()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<AcceptedPingEndpoint>(new EndpointMetadata(["POST"], "/ping-accepted"));

        // Substitute only the mediator (IInvoker), not IHttpInvoker: the point of this test is to
        // exercise the real HttpInvoker + AsHttpBuilder + WrapperHttpResult pipeline, which is what
        // silently discarded the configured status code before the onSuccess overload existed.
        var mediator = Substitute.For<IInvoker>();
        mediator.InvokeAsync(Arg.Any<AcceptedPingCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(UnambitiousFx.Functional.Result.Success()));

        var services = new ServiceCollection();
        services.AddSingleton(mediator);
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var descriptor = ((EndpointBase)new AcceptedPingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<AcceptedPingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WhenRealInvokerFails_ReturnsMappedFailureWithoutConfiguredStatusCode()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<FailingPingEndpoint>(new EndpointMetadata(["POST"], "/ping-failing"));

        var mediator = Substitute.For<IInvoker>();
        mediator.InvokeAsync(Arg.Any<FailingPingCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(UnambitiousFx.Functional.Result.Failure("boom")));

        var services = new ServiceCollection();
        services.AddSingleton(mediator);
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var descriptor = ((EndpointBase)new FailingPingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<FailingPingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert: the endpoint configures 202 for success, but the dispatch failed, so the mapped
        // failure (500, via DefaultFailureHttpMapper's catch-all) is written instead.
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    internal sealed record PingCommand : IRequest;

    internal sealed partial class PingEndpoint : Endpoint<PingCommand>;

    internal sealed record ConflictPingCommand : IRequest;

    internal sealed partial class ConflictPingEndpoint : Endpoint<ConflictPingCommand>;

    internal sealed record AcceptedPingCommand : IRequest;

    internal sealed partial class AcceptedPingEndpoint : Endpoint<AcceptedPingCommand>
    {
        public override void Configure(IEndpointBuilder builder)
        {
            builder.StatusCode(StatusCodes.Status202Accepted);
        }
    }

    internal sealed record FailingPingCommand : IRequest;

    internal sealed partial class FailingPingEndpoint : Endpoint<FailingPingCommand>
    {
        public override void Configure(IEndpointBuilder builder)
        {
            builder.StatusCode(StatusCodes.Status202Accepted);
        }
    }
}
