using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

/// <summary>
///     The mediator-bound middle level: binding is hand-written, everything downstream of it is the
///     same code the high level runs.
/// </summary>
public sealed class RawEndpointGenericTests
{
    [Fact]
    public async Task Invoke_WithHandWrittenBinding_DispatchesTheBoundMessage()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<LookupEndpoint>(new EndpointMetadata(["GET"], "/lookup/{id}"));

        LookupQuery? dispatched = null;
        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<IRequest<string>>(), Arg.Any<Func<string, IResult>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                dispatched = (LookupQuery)call.Arg<IRequest<string>>();
                return ValueTask.FromResult(call.Arg<Func<string, IResult>>()("found"));
            });

        var context = NewContext(invoker);
        context.Request.RouteValues["id"] = "42";

        var descriptor = ((EndpointBase)new LookupEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<LookupEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.NotNull(dispatched);
        Assert.Equal(42, dispatched.Id);
    }

    [Fact]
    public async Task Invoke_WhenTheHandWrittenBindingFails_Returns400WithEveryErrorAndNeverDispatches()
    {
        // Arrange — two bad query values in one request. This is the whole point of the collector:
        // the caller learns about both at once instead of fixing one and rediscovering the other.
        EndpointRegistry.RegisterMetadata<LookupEndpoint>(new EndpointMetadata(["GET"], "/lookup/{id}"));

        var invoker = Substitute.For<IHttpInvoker>();
        var context = NewContext(invoker);
        context.Request.QueryString = new QueryString("?page=nope");

        var descriptor = ((EndpointBase)new LookupEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<LookupEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);

        var errors = await ReadValidationErrorsAsync(context);
        Assert.Contains("id", errors.Keys);
        Assert.Contains("page", errors.Keys);

        await invoker.DidNotReceive().InvokeAsync(
            Arg.Any<IRequest<string>>(), Arg.Any<Func<string, IResult>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_WithDeclarativeCreated_Returns201AndALocationHeader()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<CreateEndpoint>(new EndpointMetadata(["POST"], "/things"));

        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<IRequest<string>>(), Arg.Any<Func<string, IResult>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(call.Arg<Func<string, IResult>>()("abc")));

        var context = NewContext(invoker);
        var descriptor = ((EndpointBase)new CreateEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<CreateEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.Equal("/things/abc", context.Response.Headers.Location);
    }

    [Fact]
    public async Task Invoke_WithAnOnSuccessOverride_UsesIt()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<OverridingEndpoint>(new EndpointMetadata(["GET"], "/overridden"));

        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<IRequest<string>>(), Arg.Any<Func<string, IResult>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(call.Arg<Func<string, IResult>>()("x")));

        var context = NewContext(invoker);
        var descriptor = ((EndpointBase)new OverridingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<OverridingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status205ResetContent, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_ForTheVoidArity_Returns204ByDefault()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<DeleteEndpoint>(new EndpointMetadata(["DELETE"], "/things/{id}"));

        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<IRequest>(), Arg.Any<Func<IResult>>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(call.Arg<Func<IResult>>()()));

        var context = NewContext(invoker);
        context.Request.RouteValues["id"] = "7";

        var descriptor = ((EndpointBase)new DeleteEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<DeleteEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }

    private static DefaultHttpContext NewContext(IHttpInvoker invoker)
    {
        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
    }

    private static async Task<Dictionary<string, string[]>> ReadValidationErrorsAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);

        return document.RootElement.GetProperty("errors")
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

    private sealed record LookupQuery(int Id, int? Page) : IRequest<string>;

    private sealed class LookupEndpoint : RawEndpoint<LookupQuery, string>
    {
        public override ValueTask<BindResult<LookupQuery>> BindAsync(HttpContext context)
        {
            var validation = context.Validate();
            validation.Route<int>("id", out var id);
            validation.QueryOptional<int>("page", out var page);

            return ValueTask.FromResult(validation.IsValid
                ? BindResult<LookupQuery>.Success(new LookupQuery(id, page))
                : BindResult<LookupQuery>.Failure(validation));
        }
    }

    private sealed record CreateCommand : IRequest<string>;

    private sealed class CreateEndpoint : RawEndpoint<CreateCommand, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.Created(id => $"/things/{id}");
        }

        public override ValueTask<BindResult<CreateCommand>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<CreateCommand>.Success(new CreateCommand()));
        }
    }

    private sealed record OverridingQuery : IRequest<string>;

    private sealed class OverridingEndpoint : RawEndpoint<OverridingQuery, string>
    {
        public override ValueTask<BindResult<OverridingQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<OverridingQuery>.Success(new OverridingQuery()));
        }

        public override IResult OnSuccess(string response,
            HttpContext context)
        {
            return TypedResults.StatusCode(StatusCodes.Status205ResetContent);
        }
    }

    private sealed record DeleteCommand(int Id) : IRequest;

    private sealed class DeleteEndpoint : RawEndpoint<DeleteCommand>
    {
        public override ValueTask<BindResult<DeleteCommand>> BindAsync(HttpContext context)
        {
            var validation = context.Validate();
            validation.Route<int>("id", out var id);

            return ValueTask.FromResult(validation.IsValid
                ? BindResult<DeleteCommand>.Success(new DeleteCommand(id))
                : BindResult<DeleteCommand>.Failure(validation));
        }
    }
}
