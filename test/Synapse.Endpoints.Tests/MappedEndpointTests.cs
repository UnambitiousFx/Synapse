using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.AspNetCore.Http;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class MappedEndpointTests
{
    [Fact]
    public async Task Invoke_WithMappedContracts_BindsHttpDtoAndReturnsMappedResponse()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new CreateBodyBinder());
        EndpointRegistry.RegisterMetadata<CreateEndpoint>(new EndpointMetadata(["POST"], "/things"));

        var invoker = Substitute.For<IHttpInvoker>();
        invoker.InvokeAsync(Arg.Any<IRequest<int>>(), Arg.Any<Func<int, IResult>>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult(call.Arg<Func<int, IResult>>()(7)));

        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var descriptor = ((EndpointBase)new CreateEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<CreateEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WhenBindingFails_Returns400BeforeInvokerIsCalled()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new FailingBodyBinder());
        EndpointRegistry.RegisterMetadata<FailingEndpoint>(new EndpointMetadata(["POST"], "/things-fail"));

        var invoker = Substitute.For<IHttpInvoker>();

        var services = new ServiceCollection();
        services.AddSingleton(invoker);
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var descriptor = ((EndpointBase)new FailingEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<FailingEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        await invoker.DidNotReceiveWithAnyArgs()
            .InvokeAsync(Arg.Any<IRequest<int>>(), Arg.Any<Func<int, IResult>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_WithConfiguredCreatedMapping_ReturnsMappedHttpResponseThroughRealHttpInvoker()
    {
        // Arrange: substitute only the mediator (IInvoker), not IHttpInvoker, so the real HttpInvoker
        // and real DefaultFailureHttpMapper are exercised. This is the path the generic onSuccess
        // overload takes; inspection says it returns onSuccess(value!) directly with no wrapping, but
        // Task 8 showed inspection alone can miss a wrapper, so this proves it through the real pipeline.
        EndpointRegistry.RegisterBinder(new CreatedBodyBinder());
        EndpointRegistry.RegisterMetadata<CreatedEndpoint>(new EndpointMetadata(["POST"], "/things-created"));

        var mediator = Substitute.For<IInvoker>();
        mediator.InvokeAsync(Arg.Any<CreatedCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(UnambitiousFx.Functional.Result.Success(42)));

        var services = new ServiceCollection();
        services.AddSingleton(mediator);
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var descriptor = ((EndpointBase)new CreatedEndpoint())
            .CreateDescriptor(EndpointRegistry.GetMetadata<CreatedEndpoint>());

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.Equal("/things/42", context.Response.Headers.Location);

        context.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<CreatedResponse>(
            context.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("thing-42", body!.Id);
    }

    private sealed record CreateBody(string Name);

    private sealed record CreateCommand(string Name) : IRequest<int>;

    private sealed record CreateResponse(string Id);

    private sealed class CreateEndpoint : MappedEndpoint<CreateBody, CreateCommand, int, CreateResponse>
    {
        public override CreateCommand ToRequest(CreateBody request)
        {
            return new CreateCommand(request.Name);
        }

        public override CreateResponse ToResponse(int response)
        {
            return new CreateResponse(response.ToString());
        }
    }

    private sealed class CreateBodyBinder : IEndpointBinder<CreateBody>
    {
        public ValueTask<BindResult<CreateBody>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<CreateBody>.Success(new CreateBody("thing")));
        }
    }

    private sealed record FailingCreateCommand(string Name) : IRequest<int>;

    private sealed record FailingCreateResponse(string Id);

    private sealed class FailingEndpoint : MappedEndpoint<CreateBody, FailingCreateCommand, int, FailingCreateResponse>
    {
        public override FailingCreateCommand ToRequest(CreateBody request)
        {
            return new FailingCreateCommand(request.Name);
        }

        public override FailingCreateResponse ToResponse(int response)
        {
            return new FailingCreateResponse(response.ToString());
        }
    }

    private sealed class FailingBodyBinder : IEndpointBinder<CreateBody>
    {
        public ValueTask<BindResult<CreateBody>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<CreateBody>.Failure("'name' is required."));
        }
    }

    private sealed record CreatedCommand(string Name) : IRequest<int>;

    private sealed record CreatedResponse(string Id);

    private sealed class CreatedEndpoint : MappedEndpoint<CreateBody, CreatedCommand, int, CreatedResponse>
    {
        public override CreatedCommand ToRequest(CreateBody request)
        {
            return new CreatedCommand(request.Name);
        }

        public override CreatedResponse ToResponse(int response)
        {
            return new CreatedResponse($"thing-{response}");
        }

        public override void Configure(IEndpointBuilder<CreatedResponse> builder)
        {
            builder.Created(response => $"/things/{response.Id.Split('-')[1]}");
        }
    }

    private sealed class CreatedBodyBinder : IEndpointBinder<CreateBody>
    {
        public ValueTask<BindResult<CreateBody>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<CreateBody>.Success(new CreateBody("thing")));
        }
    }
}
