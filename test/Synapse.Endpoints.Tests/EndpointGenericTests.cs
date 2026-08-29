using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class EndpointGenericTests
{
    [Fact]
    public async Task Invoke_WithDefaultConfiguration_Returns200AndTheResponse()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new EchoBinder());
        EndpointRegistry.RegisterMetadata<EchoEndpoint>(new EndpointMetadata(["GET"], "/echo"));

        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<IRequest<string>>(), Arg.Any<Func<string, IResult>>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(call.Arg<Func<string, IResult>>()("hello")));

        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var endpoint = new EchoEndpoint();
        var descriptor = ((EndpointBase)endpoint).CreateDescriptor(EndpointRegistry.GetMetadata<EchoEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WhenBindingFails_Returns400()
    {
        // Arrange
        EndpointRegistry.RegisterBinder<FailingQuery>(new FailingBinder());
        EndpointRegistry.RegisterMetadata<FailingEndpoint>(new EndpointMetadata(["GET"], "/fail"));

        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IHttpInvoker>());
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var descriptor = ((EndpointBase)new FailingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<FailingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    private sealed record EchoQuery : IRequest<string>;

    private sealed class EchoEndpoint : Endpoint<EchoQuery, string>;

    private sealed class EchoBinder : IEndpointBinder<EchoQuery>
    {
        public ValueTask<BindResult<EchoQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<EchoQuery>.Success(new EchoQuery()));
        }
    }

    private sealed record FailingQuery : IRequest<string>;

    private sealed class FailingEndpoint : Endpoint<FailingQuery, string>;

    private sealed class FailingBinder : IEndpointBinder<FailingQuery>
    {
        public ValueTask<BindResult<FailingQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<FailingQuery>.Failure("id", "is not a valid Guid."));
        }
    }
}
