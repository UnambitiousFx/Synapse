using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class EndpointGenericTests
{
    [Fact]
    public async Task Invoke_WithDefaultConfiguration_Returns200AndTheResponse()
    {
        // Arrange
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

    internal sealed record EchoQuery : IRequest<string>;

    [Get("/echo")]
    internal sealed partial class EchoEndpoint : Endpoint<EchoQuery, string>;

    internal sealed record FailingQuery : IRequest<string>;

    /// <summary>
    ///     A hand-written failing binding. Was a stubbed IEndpointBinder registered against
    ///     Endpoint&lt;FailingQuery, string&gt;; the binding is generated now, so the failure is
    ///     expressed at the tier that exists for hand-written binding. Behaviourally identical — the
    ///     two tiers differ only in where BindAsync comes from.
    /// </summary>
    [Get("/fail")]
    internal sealed partial class FailingEndpoint : RawEndpoint<FailingQuery, string>
    {
        public override ValueTask<BindResult<FailingQuery>> BindAsync(HttpContext context)
        {
            return new(BindResult<FailingQuery>.Failure("id", "is not a valid Guid."));
        }
    }
}
