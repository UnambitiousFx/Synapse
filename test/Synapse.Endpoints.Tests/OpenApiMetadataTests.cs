using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;
using UnambitiousFx.Synapse.Endpoints.Internal;

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

    // Pins the wider bodyless set. docs/docs/endpoints/reference/base-classes.mdx points at [HttpEndpoint("OPTIONS", …)] as
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
        // Asserted on the library's own metadata type rather than on every
        // IProducesResponseTypeMetadata on the endpoint, because what the framework infers from the
        // shape of the mapping lambda is target-framework-dependent: net9.0 adds a
        // "200, System.Void, text/plain" entry of its own and net10.0 adds nothing. Filtering to
        // ProducesResponseMetadata pins what this library declares, which is the thing under test.
        var declared = endpoint.Metadata.OfType<ProducesResponseMetadata>().ToArray();

        // Exactly one declaration, and it is the 204 — not a default 200.
        var single = Assert.Single(declared);
        Assert.Equal(StatusCodes.Status204NoContent, single.StatusCode);

        // Described as void rather than null. Microsoft.AspNetCore.OpenApi skips a null-Type entry
        // outright, so while the metadata was present the declared 204 never reached
        // /openapi/v1.json — see docs/known-issues/051.
        Assert.Equal(typeof(void), single.Type);
        Assert.Empty(single.ContentTypes);
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

    // A declarative mapper that writes no body must not declare one. NoContent() and StatusCode(int)
    // set only the status code, so declaring typeof(TResponse) alongside them put a JSON schema on a
    // response that never carries a body — invalid for a 204, and a client generator would model a
    // return value that never arrives. See docs/known-issues/054.
    [Theory]
    [InlineData(true, StatusCodes.Status204NoContent)]
    [InlineData(false, StatusCodes.Status304NotModified)]
    public void CreateDescriptor_ForABodylessSuccessMapper_DeclaresNoResponseBody(bool noContent, int expected)
    {
        // Arrange
        BodylessMapperEndpoint.UseNoContent = noContent;
        EndpointRegistry.RegisterBinder(new BodylessMetaBinder());
        EndpointRegistry.RegisterMetadata<BodylessMapperEndpoint>(
            new EndpointMetadata(["GET"], "/meta-bodyless-mapper"));
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<BodylessMapperEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var declared = endpoint.Metadata.OfType<ProducesResponseMetadata>().Single();

        Assert.Equal(expected, declared.StatusCode);
        Assert.Equal(typeof(void), declared.Type);
        Assert.Empty(declared.ContentTypes);
    }

    // The counterpart: a mapper that does write a body still declares it, so the fix above did not
    // simply stop declaring response types.
    [Fact]
    public void CreateDescriptor_ForASuccessMapperWithABody_StillDeclaresTheResponseType()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new BodylessMetaBinder());
        EndpointRegistry.RegisterMetadata<CreatedMapperEndpoint>(
            new EndpointMetadata(["GET"], "/meta-created-mapper"));
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<CreatedMapperEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var declared = endpoint.Metadata.OfType<ProducesResponseMetadata>().Single();

        Assert.Equal(StatusCodes.Status201Created, declared.StatusCode);
        Assert.Equal(typeof(string), declared.Type);
        Assert.Equal(["application/json"], declared.ContentTypes);
    }

    // Binding failures answer with HttpValidationProblemDetails — a problem document plus an errors
    // dictionary — since the accumulating binders landed. The declared 400 was still a plain
    // ProblemDetails, so the document described a narrower body than the endpoint sends. See
    // docs/known-issues/055.
    [Fact]
    public void CreateDescriptor_DeclaresTheValidationProblemItActuallySendsForA400()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new BodylessMetaBinder());
        EndpointRegistry.RegisterMetadata<BodylessMetaEndpoint>(
            new EndpointMetadata(["GET"], "/meta-problem-shape"));
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<BodylessMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var badRequest = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status400BadRequest);

        Assert.Equal(typeof(HttpValidationProblemDetails), badRequest.Type);
        Assert.Equal(["application/problem+json"], badRequest.ContentTypes);
    }

    private sealed class BodylessMapperEndpoint : Endpoint<BodylessMetaQuery, string>
    {
        internal static bool UseNoContent { get; set; }

        public override void Configure(IEndpointBuilder<string> builder)
        {
            if (UseNoContent)
            {
                builder.NoContent();
            }
            else
            {
                builder.StatusCode(StatusCodes.Status304NotModified);
            }
        }
    }

    private sealed class CreatedMapperEndpoint : Endpoint<BodylessMetaQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.Created(value => $"/things/{value}");
        }
    }

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
