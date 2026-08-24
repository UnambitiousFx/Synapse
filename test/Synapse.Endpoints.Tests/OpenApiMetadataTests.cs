using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed class OpenApiMetadataTests
{
    [Fact]
    public void CreateDescriptor_ForRequestWithResponse_DeclaresAcceptsAndProduces()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new MetaBinder());
        EndpointRegistry.RegisterMetadata<MetaEndpoint>(new EndpointMetadata(["POST"], "/meta"));
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<MetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var produces = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().ToArray();
        Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status200OK &&
                                               metadata.Type == typeof(string));
        Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status400BadRequest);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
        Assert.NotNull(endpoint.Metadata.GetMetadata<MethodInfo>());
    }

    [Fact]
    public void CreateDescriptor_ForBodylessVerb_DeclaresNoAcceptsMetadata()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new BodylessMetaBinder());
        EndpointRegistry.RegisterMetadata<BodylessMetaEndpoint>(new EndpointMetadata(["GET"], "/meta-get"));
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<BodylessMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        Assert.Null(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
    }

    // Pins the wider bodyless set. docs/docs/endpoints.mdx points at [HttpEndpoint("OPTIONS", …)] as
    // the way to declare the verbs with no dedicated attribute, and neither OPTIONS nor TRACE carries
    // a request body — but both used to be treated as body-carrying, declaring Accepts for a body no
    // such request can send (and, on the generator side, emitting a read for it).
    [Theory]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public void CreateDescriptor_ForOptionsOrTraceVerb_DeclaresNoAcceptsMetadata(string verb)
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new BodylessMetaBinder());
        EndpointRegistry.RegisterMetadata<BodylessMetaEndpoint>(new EndpointMetadata([verb], "/meta-bodyless"));
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<BodylessMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        Assert.Null(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
    }

    // Settles the multi-verb question the bodyless check leaves open, deliberately and while it is
    // still free to settle: the rule is "all declared verbs are bodyless", not "any of them is". An
    // endpoint serving both GET and POST really does accept a JSON body on one of them, so Accepts is
    // the accurate declaration; under "any" it would have been silently omitted. Nothing in the
    // public API can currently produce more than one verb (a route attribute carries a single method,
    // and EndpointBuilder.Route assigns a single-element array), so this test reaches past the
    // builder and constructs the metadata directly — it pins the decision for whoever adds multi-verb
    // support, and would otherwise be untestable.
    [Fact]
    public void CreateDescriptor_ForMultiVerbEndpointMixingBodylessAndBodyCarrying_DeclaresAcceptsMetadata()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new MultiVerbMetaBinder());
        EndpointRegistry.RegisterMetadata<MultiVerbMetaEndpoint>(
            new EndpointMetadata(["GET", "POST"], "/meta-multi"));
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<MultiVerbMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
    }

    [Fact]
    public void CreateDescriptor_ForEndpointConfiguredCreated_Declares201NotDefault200()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new CreatedMetaBinder());
        EndpointRegistry.RegisterMetadata<CreatedMetaEndpoint>(new EndpointMetadata(["POST"], "/meta-created"));
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<CreatedMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var produces = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().ToArray();
        Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status201Created &&
                                               metadata.Type == typeof(string));

        // The framework itself contributes a default "200, System.Void" entry inferred from the
        // mapping delegate's return type; that is unrelated to our declared response type and must
        // not be confused with a dishonest 200 for the actual TResponse.
        Assert.DoesNotContain(produces, metadata => metadata.StatusCode == StatusCodes.Status200OK &&
                                                     metadata.Type == typeof(string));
    }

    [Fact]
    public void CreateDescriptor_ForMappedEndpointConfiguredCreated_Declares201NotDefault200()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new CreatedMappedRequestBinder());
        EndpointRegistry.RegisterMetadata<CreatedMappedEndpoint>(new EndpointMetadata(["POST"], "/meta-mapped-created"));
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<CreatedMappedEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var produces = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().ToArray();
        Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status201Created &&
                                               metadata.Type == typeof(CreatedMappedResponse));
        Assert.DoesNotContain(produces, metadata => metadata.StatusCode == StatusCodes.Status200OK &&
                                                     metadata.Type == typeof(CreatedMappedResponse));

        // Proves the non-generic Accepts(Type, ...) overload (used so THttpRequest need not carry a
        // notnull constraint) declares the same request type the generic overload would have.
        var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
        Assert.NotNull(accepts);
        Assert.Equal(typeof(CreatedMappedRequest), accepts!.RequestType);
    }

    [Fact]
    public void CreateDescriptor_ForEndpointWithNoResponse_Declares204NotDefault200()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new VoidMetaBinder());
        EndpointRegistry.RegisterMetadata<VoidMetaEndpoint>(new EndpointMetadata(["POST"], "/meta-void"));
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<VoidMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var produces = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().ToArray();
        Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status204NoContent &&
                                               metadata.Type is null);

        // Distinguished from the framework's own default "200, System.Void" entry by Type: ours for
        // a no-response endpoint is null, the framework's inferred one is typeof(void).
        Assert.DoesNotContain(produces, metadata => metadata.StatusCode == StatusCodes.Status200OK &&
                                                     metadata.Type is null);
    }

    [Fact]
    public void CreateDescriptor_ForStreamEndpoint_DeclaresJsonAndEventStreamProduces()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new StreamMetaBinder());
        EndpointRegistry.RegisterMetadata<StreamMetaEndpoint>(new EndpointMetadata(["GET"], "/meta-stream"));
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<StreamMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var produces = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Where(metadata => metadata.StatusCode == StatusCodes.Status200OK)
            .ToArray();
        Assert.Contains(produces, metadata => metadata.ContentTypes.Contains("application/json"));
        Assert.Contains(produces, metadata => metadata.ContentTypes.Contains("text/event-stream"));
    }

    private sealed record MetaQuery : IRequest<string>;

    private sealed class MetaEndpoint : Endpoint<MetaQuery, string>;

    private sealed class MetaBinder : IEndpointBinder<MetaQuery>
    {
        public ValueTask<BindResult<MetaQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<MetaQuery>.Success(new MetaQuery()));
        }
    }

    private sealed record BodylessMetaQuery : IRequest<string>;

    private sealed class BodylessMetaEndpoint : Endpoint<BodylessMetaQuery, string>;

    private sealed class BodylessMetaBinder : IEndpointBinder<BodylessMetaQuery>
    {
        public ValueTask<BindResult<BodylessMetaQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<BodylessMetaQuery>.Success(new BodylessMetaQuery()));
        }
    }

    private sealed record MultiVerbMetaQuery : IRequest<string>;

    private sealed class MultiVerbMetaEndpoint : Endpoint<MultiVerbMetaQuery, string>;

    private sealed class MultiVerbMetaBinder : IEndpointBinder<MultiVerbMetaQuery>
    {
        public ValueTask<BindResult<MultiVerbMetaQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<MultiVerbMetaQuery>.Success(new MultiVerbMetaQuery()));
        }
    }

    private sealed record CreatedMetaCommand : IRequest<string>;

    private sealed class CreatedMetaEndpoint : Endpoint<CreatedMetaCommand, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.Created(response => $"/meta-created/{response}");
        }
    }

    private sealed class CreatedMetaBinder : IEndpointBinder<CreatedMetaCommand>
    {
        public ValueTask<BindResult<CreatedMetaCommand>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<CreatedMetaCommand>.Success(new CreatedMetaCommand()));
        }
    }

    private sealed record CreatedMappedRequest(string Name);

    private sealed record CreatedMappedCommand(string Name) : IRequest<int>;

    private sealed record CreatedMappedResponse(string Id);

    private sealed class CreatedMappedEndpoint
        : MappedEndpoint<CreatedMappedRequest, CreatedMappedCommand, int, CreatedMappedResponse>
    {
        public override CreatedMappedCommand ToRequest(CreatedMappedRequest request)
        {
            return new CreatedMappedCommand(request.Name);
        }

        public override CreatedMappedResponse ToResponse(int response)
        {
            return new CreatedMappedResponse(response.ToString());
        }

        public override void Configure(IEndpointBuilder<CreatedMappedResponse> builder)
        {
            builder.Created(response => $"/meta-mapped-created/{response.Id}");
        }
    }

    private sealed class CreatedMappedRequestBinder : IEndpointBinder<CreatedMappedRequest>
    {
        public ValueTask<BindResult<CreatedMappedRequest>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<CreatedMappedRequest>.Success(new CreatedMappedRequest("thing")));
        }
    }

    private sealed record VoidMetaCommand : IRequest;

    private sealed class VoidMetaEndpoint : Endpoint<VoidMetaCommand>;

    private sealed class VoidMetaBinder : IEndpointBinder<VoidMetaCommand>
    {
        public ValueTask<BindResult<VoidMetaCommand>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<VoidMetaCommand>.Success(new VoidMetaCommand()));
        }
    }

    private sealed record StreamMetaQuery : IStreamRequest<int>;

    private sealed class StreamMetaEndpoint : StreamEndpoint<StreamMetaQuery, int>;

    private sealed class StreamMetaBinder : IEndpointBinder<StreamMetaQuery>
    {
        public ValueTask<BindResult<StreamMetaQuery>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(BindResult<StreamMetaQuery>.Success(new StreamMetaQuery()));
        }
    }
}
