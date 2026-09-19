using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

/// <summary>
///     The void arity of the contract tier: a wire DTO mapped onto a command with no response, which
///     before this tier existed had to borrow <c>ContractEndpoint&lt;…&gt;</c>'s four type arguments
///     and invent a response nobody wanted.
/// </summary>
public sealed partial class ContractEndpointVoidTests
{
    [Fact]
    public async Task Invoke_WithMappedContract_DispatchesMappedCommandAndReturns204()
    {
        // Arrange
        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<ArchiveCommand>(), Arg.Any<Func<IResult>>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(call.Arg<Func<IResult>>()()));

        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();
        var context = NewJsonBodyContext(services, """{"reason":"stale"}""");

        var endpoint = new ArchiveEndpoint();
        var descriptor = ((SynapseEndpoint)endpoint).CreateDescriptor(endpoint.Metadata);

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        await invoker.Received(1)
            .InvokeAsync(
                Arg.Is<ArchiveCommand>(command => command.Reason == "stale"),
                Arg.Any<Func<IResult>>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_WhenBindingFails_Returns400BeforeInvokerIsCalled()
    {
        // Arrange — no body and no content type, which is what the generated binding rejects: this
        // tier binds ArchiveBody from the JSON body, so a bodyless POST cannot produce one.
        var invoker = Substitute.For<IHttpInvoker>();

        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var endpoint = new FailingArchiveEndpoint();
        var descriptor = ((SynapseEndpoint)endpoint).CreateDescriptor(endpoint.Metadata);

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        await invoker.DidNotReceiveWithAnyArgs()
            .InvokeAsync(Arg.Any<FailingArchiveCommand>(), Arg.Any<Func<IResult>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_WithConfiguredStatusCode_Returns202ThroughRealHttpInvoker()
    {
        // Arrange: substitute only the mediator (IInvoker), so the real HttpInvoker and the real
        // failure mapper run — the path that decides whether a configured status survives.
        var mediator = Substitute.For<IInvoker>();
        mediator.InvokeAsync(Arg.Any<AcceptedArchiveCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(UnambitiousFx.Functional.Result.Success()));

        var services = new ServiceCollection();
        services.AddSingleton(mediator);
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var context = NewJsonBodyContext(services, """{"reason":"stale"}""");

        var endpoint = new AcceptedArchiveEndpoint();
        var descriptor = ((SynapseEndpoint)endpoint).CreateDescriptor(endpoint.Metadata);

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
    }

    /// <summary>A context carrying <paramref name="body" /> as a JSON request body.</summary>
    private static DefaultHttpContext NewJsonBodyContext(IServiceCollection services,
        string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();
        return context;
    }

    internal sealed record ArchiveBody(string Reason);

    internal sealed record ArchiveCommand(string Reason) : IRequest;

    [Post("/things/archive")]
    internal sealed partial class ArchiveEndpoint : ContractEndpoint<ArchiveBody, ArchiveCommand>
    {
        public override ArchiveCommand ToRequest(ArchiveBody request)
        {
            return new ArchiveCommand(request.Reason);
        }
    }

    internal sealed record FailingArchiveCommand(string Reason) : IRequest;

    [Post("/things/archive-fail")]
    internal sealed partial class FailingArchiveEndpoint : ContractEndpoint<ArchiveBody, FailingArchiveCommand>
    {
        public override FailingArchiveCommand ToRequest(ArchiveBody request)
        {
            return new FailingArchiveCommand(request.Reason);
        }
    }

    internal sealed record AcceptedArchiveCommand(string Reason) : IRequest;

    [Post("/things/archive-accepted")]
    internal sealed partial class AcceptedArchiveEndpoint : ContractEndpoint<ArchiveBody, AcceptedArchiveCommand>
    {
        public override AcceptedArchiveCommand ToRequest(ArchiveBody request)
        {
            return new AcceptedArchiveCommand(request.Reason);
        }

        public override void Configure(IEndpointBuilder builder)
        {
            builder.StatusCode(StatusCodes.Status202Accepted);
        }
    }
}
