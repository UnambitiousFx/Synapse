using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class EndpointVoidTests
{
    [Fact]
    public async Task Invoke_WithDefaultConfiguration_Returns204()
    {
        // Arrange
        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<PingCommand>(), Arg.Any<Func<Microsoft.AspNetCore.Http.IResult>>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(call.Arg<Func<Microsoft.AspNetCore.Http.IResult>>()()));

        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var endpoint = new PingEndpoint();
        var descriptor = ((EndpointBase)endpoint).CreateDescriptor(endpoint.Metadata);

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WhenInvokerReturnsMappedFailure_Returns409WithoutInvokingOnSuccess()
    {
        // Arrange
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

        var endpoint = new ConflictPingEndpoint();
        var descriptor = ((EndpointBase)endpoint).CreateDescriptor(endpoint.Metadata);

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WithConfiguredStatusCode_Returns202ThroughRealHttpInvoker()
    {
        // Arrange
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

        var endpoint = new AcceptedPingEndpoint();
        var descriptor = ((EndpointBase)endpoint).CreateDescriptor(endpoint.Metadata);

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WhenRealInvokerFails_ReturnsMappedFailureWithoutConfiguredStatusCode()
    {
        // Arrange
        var mediator = Substitute.For<IInvoker>();
        mediator.InvokeAsync(Arg.Any<FailingPingCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(UnambitiousFx.Functional.Result.Failure("boom")));

        var services = new ServiceCollection();
        services.AddSingleton(mediator);
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var endpoint = new FailingPingEndpoint();
        var descriptor = ((EndpointBase)endpoint).CreateDescriptor(endpoint.Metadata);

        // Act
        await descriptor.InvokeAsync(context);

        // Assert: the endpoint configures 202 for success, but the dispatch failed, so the mapped
        // failure (500, via DefaultFailureHttpMapper's catch-all) is written instead.
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    internal sealed record PingCommand : IRequest;

    [Post("/ping")]
    internal sealed partial class PingEndpoint : Endpoint<PingCommand>;

    internal sealed record ConflictPingCommand : IRequest;

    [Post("/ping-conflict")]
    internal sealed partial class ConflictPingEndpoint : Endpoint<ConflictPingCommand>;

    internal sealed record AcceptedPingCommand : IRequest;

    [Post("/ping-accepted")]
    internal sealed partial class AcceptedPingEndpoint : Endpoint<AcceptedPingCommand>
    {
        public override void Configure(IEndpointBuilder builder)
        {
            builder.StatusCode(StatusCodes.Status202Accepted);
        }
    }

    internal sealed record FailingPingCommand : IRequest;

    [Post("/ping-failing")]
    internal sealed partial class FailingPingEndpoint : Endpoint<FailingPingCommand>
    {
        public override void Configure(IEndpointBuilder builder)
        {
            builder.StatusCode(StatusCodes.Status202Accepted);
        }
    }
}
