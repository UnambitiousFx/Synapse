using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

/// <summary>
///     The free-form low level: the endpoint gets the context and returns its own result.
/// </summary>
public sealed partial class RawEndpointTests
{
    [Fact]
    public async Task Invoke_ExecutesTheResultTheHandlerReturned()
    {
        // Arrange
        var context = NewContext();
        var endpoint = new TeapotEndpoint();
        var descriptor = ((SynapseEndpoint)endpoint).CreateDescriptor(endpoint.Metadata);

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status418ImATeapot, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_PassesTheHttpContextAndTheRequestCancellationToken()
    {
        // Arrange
        var context = NewContext();
        context.Request.Headers["X-Trace"] = "abc123";
        using var aborted = new CancellationTokenSource();
        context.RequestAborted = aborted.Token;

        var endpoint = new EchoHeaderEndpoint();
        var descriptor = ((SynapseEndpoint)endpoint)
            .CreateDescriptor(endpoint.Metadata);

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal("abc123", endpoint.SeenHeader);
        Assert.Equal(aborted.Token, endpoint.SeenToken);
    }

    [Fact]
    public async Task Invoke_ResolvesServicesFromTheRequestScope()
    {
        // Arrange — endpoints are startup singletons with no constructor injection, so a low-level
        // handler's only route to a dependency is context.Service<T>().
        var context = NewContext(services => services.AddSingleton(new Greeter("hi there")));

        var endpoint = new GreetingEndpoint();
        var descriptor = ((SynapseEndpoint)endpoint)
            .CreateDescriptor(endpoint.Metadata);

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal("hi there", endpoint.SeenGreeting);
    }

    [Fact]
    public void CreateDescriptor_TakesTheRouteFromTheAttribute()
    {
        // Act
        var endpoint = new TeapotEndpoint();
        var descriptor = ((SynapseEndpoint)endpoint).CreateDescriptor(endpoint.Metadata);

        // Assert
        Assert.Equal("/teapot", descriptor.Route);
        Assert.Equal(["GET"], descriptor.HttpMethods);
    }

    [Fact]
    public void CreateDescriptor_TakesTheRouteFromConfigure_WhenTheAttributeDeclaredNone()
    {
        // Act
        var endpoint = new ConfiguredRouteEndpoint();
        var descriptor = ((SynapseEndpoint)endpoint).CreateDescriptor(endpoint.Metadata);

        // Assert
        Assert.Equal("/computed", descriptor.Route);
        Assert.Equal(["POST"], descriptor.HttpMethods);
    }

    [Fact]
    public void CreateDescriptor_WithNoRouteAnywhere_ThrowsActionably()
    {
        // Arrange
        var endpoint = new RoutelessEndpoint();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => ((SynapseEndpoint)endpoint).CreateDescriptor(endpoint.Metadata));

        // Assert — the same message the high level gives, because both resolve the route in one place.
        Assert.Contains("declares no route", exception.Message);
    }

    [Fact]
    public void Map_DeclaresOnlyTheMetadataTheEndpointAskedFor()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<DeclaringEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var produces = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().ToArray();

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
        Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status202Accepted);
        Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status200OK &&
                                              metadata.Type == typeof(Greeting));

        // The RDG needs a MethodInfo for the endpoint to appear in the OpenAPI document at all.
        Assert.NotNull(endpoint.Metadata.GetMetadata<MethodInfo>());
    }

    [Fact]
    public void Map_ForAnEndpointThatDeclaresNothing_ProducesNoResponseMetadata()
    {
        // Arrange — nothing can be inferred for a hand-written handler, and in particular no 400 is
        // declared: unlike the higher tiers, a low-level endpoint binds nothing, so it does not
        // necessarily have a 400 to advertise.
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<TeapotEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();

        var produces = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().ToArray();

        // ASP.NET Core's own request-delegate generator infers one entry (200, void, text/plain) from
        // the shape of the mapping lambda, for every Synapse endpoint alike. What matters here is that
        // the library adds nothing on top: no typed response, and in particular no 400.
        Assert.DoesNotContain(produces, metadata => metadata.Type is not null && metadata.Type != typeof(void));
        Assert.DoesNotContain(produces, metadata => metadata.StatusCode == StatusCodes.Status400BadRequest);
        Assert.Null(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
    }

    private static DefaultHttpContext NewContext(Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
    }

    internal sealed record Greeting(string Text);

    private sealed class Greeter
    {
        internal Greeter(string text)
        {
            Text = text;
        }

        internal string Text { get; }
    }

    [Get("/teapot")]
    internal sealed partial class TeapotEndpoint : RawEndpoint
    {
        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(TypedResults.StatusCode(StatusCodes.Status418ImATeapot) as IResult);
        }
    }

    [Get("/echo-header")]
    internal sealed partial class EchoHeaderEndpoint : RawEndpoint
    {
        internal string? SeenHeader { get; private set; }

        internal CancellationToken SeenToken { get; private set; }

        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            SeenHeader = context.Header("X-Trace");
            SeenToken = cancellationToken;
            return ValueTask.FromResult(TypedResults.NoContent() as IResult);
        }
    }

    [Get("/greeting")]
    internal sealed partial class GreetingEndpoint : RawEndpoint
    {
        internal string? SeenGreeting { get; private set; }

        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            SeenGreeting = context.Service<Greeter>().Text;
            return ValueTask.FromResult(TypedResults.NoContent() as IResult);
        }
    }

    // HandleAsync is public API of the low tier, so returning null is a mistake user code can make.
    // Executing it produced a bare NullReferenceException out of the request delegate — a 500 naming
    // neither the endpoint nor the cause. See docs/known-issues/056.
    [Fact]
    public async Task Invoke_WhenTheHandlerReturnsNull_ThrowsNamingTheEndpointAndTheRemedy()
    {
        // Arrange
        var context = NewContext();
        var endpoint = new NullResultEndpoint();
        var descriptor = ((SynapseEndpoint)endpoint).CreateDescriptor(endpoint.Metadata);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await descriptor.InvokeAsync(context));

        // Assert
        Assert.Contains(nameof(NullResultEndpoint), exception.Message);
        Assert.Contains("TypedResults", exception.Message);
    }

    // The other half of the same problem: request-time state is created when the endpoint is mapped,
    // so calling the public handler on a bare instance — the natural way to try to unit-test one —
    // used to dereference a null field. See docs/known-issues/056.
    [Fact]
    public async Task HandleAsync_OnAnUnmappedEndpoint_ExplainsThatItWasNeverMapped()
    {
        // Arrange
        var endpoint = new UnmappedEndpoint();

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await endpoint.HandleAsync(NewContext(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(nameof(UnmappedEndpoint), exception.Message);
        Assert.Contains("has not been mapped", exception.Message);
    }

    [Get("/null-result")]
    internal sealed partial class NullResultEndpoint : RawEndpoint
    {
        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IResult>(null!);
        }
    }

    internal sealed partial class UnmappedEndpoint : BoundEndpoint<UnmappedCommand>
    {
        public override ValueTask<BindResult<UnmappedCommand>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<UnmappedCommand>.Success(new UnmappedCommand()));
        }
    }

    internal sealed record UnmappedCommand : IRequest;

    internal sealed partial class ConfiguredRouteEndpoint : RawEndpoint
    {
        public override void Configure(IRawEndpointBuilder builder)
        {
            builder.Post("/computed");
        }

        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(TypedResults.NoContent() as IResult);
        }
    }

    internal sealed partial class RoutelessEndpoint : RawEndpoint
    {
        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(TypedResults.NoContent() as IResult);
        }
    }

    [Post("/declared")]
    internal sealed partial class DeclaringEndpoint : RawEndpoint
    {
        public override void Configure(IRawEndpointBuilder builder)
        {
            builder.Accepts<Greeting>()
                .Produces<Greeting>()
                .Produces(StatusCodes.Status202Accepted)
                .Tag("Raw");
        }

        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(TypedResults.Accepted((string?)null) as IResult);
        }
    }
}
