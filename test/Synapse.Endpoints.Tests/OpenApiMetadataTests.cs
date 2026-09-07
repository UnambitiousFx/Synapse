using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;
using UnambitiousFx.Synapse.Endpoints.Internal;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class OpenApiMetadataTests
{
    [Fact]
    public void CreateDescriptor_ForRequestWithResponse_DeclaresAcceptsAndProduces()
    {
        // Arrange
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
    // such request can send (and, on the generator side, emitting a read for it). Two facts rather
    // than a theory over the verb: the verb is a property of the endpoint type now, so it cannot come
    // from an [InlineData].
    [Fact]
    public void CreateDescriptor_ForOptionsVerb_DeclaresNoAcceptsMetadata()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<OptionsMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        Assert.Null(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
    }

    [Fact]
    public void CreateDescriptor_ForTraceVerb_DeclaresNoAcceptsMetadata()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<TraceMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        Assert.Null(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
    }

    [Fact]
    public void CreateDescriptor_ForEndpointConfiguredCreated_Declares201NotDefault200()
    {
        // Arrange
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
    public void CreateDescriptor_ForSelfHandledEndpoint_DeclaresTheSameMetadataAsTheDispatchingTier()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<SelfHandledMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var produces = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().ToArray();
        Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status200OK &&
                                              metadata.Type == typeof(string));

        // The 400 is guaranteed for the same reason it is on Endpoint<…>: something binds, so a bad
        // request answers with a validation problem rather than reaching the handler.
        Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status400BadRequest);

        var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
        Assert.NotNull(accepts);
        Assert.Equal(typeof(SelfHandledMetaRequest), accepts!.RequestType);
    }

    [Fact]
    public void CreateDescriptor_ForSelfHandledEndpointWithNoResponse_Declares204NotDefault200()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<SelfHandledVoidMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var single = Assert.Single(endpoint.Metadata.OfType<ProducesResponseMetadata>());
        Assert.Equal(StatusCodes.Status204NoContent, single.StatusCode);
        Assert.Equal(typeof(void), single.Type);
    }

    [Fact]
    public void CreateDescriptor_ForStreamEndpoint_DeclaresJsonAndEventStreamProduces()
    {
        // Arrange
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

    // StreamEndpoint was the one body-carrying tier that never declared what it accepts: a POST
    // stream binds TRequest by deserializing the request body exactly as the single-response tiers do,
    // but published no requestBody and so could not have a wrong content type rejected by routing.
    // See docs/known-issues/065.
    [Fact]
    public void CreateDescriptor_ForStreamEndpointOnABodyCarryingVerb_DeclaresAcceptsMetadata()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<PostStreamMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
        Assert.NotNull(accepts);
        Assert.Equal(typeof(PostStreamMetaQuery), accepts.RequestType);
        Assert.Contains("application/json", accepts.ContentTypes);
    }

    // The guard is on the verb, so the common bodyless stream keeps declaring nothing.
    [Fact]
    public void CreateDescriptor_ForStreamEndpointOnABodylessVerb_DeclaresNoAcceptsMetadata()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<StreamMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        Assert.Null(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
    }

    // docs/known-issues/067: the verb is not what decides whether a body is read, so it cannot be
    // what decides whether one is declared. A POST whose binder reads nothing must declare nothing,
    // or the document promises a schema the endpoint ignores and routing rejects a content type it
    // never looks at.
    [Fact]
    public void CreateDescriptor_ForABodyCarryingVerbWhoseBinderReadsNoBody_DeclaresNoAcceptsMetadata()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<NoBodyMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        Assert.Null(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
    }

    // The hand-bound middle tier has no generated binder to ask, so it keeps declaring from the verb:
    // its author writes BindAsync and may read a body on any verb that carries one.
    [Fact]
    public void CreateDescriptor_ForAHandBoundEndpointOnAPost_StillDeclaresAcceptsMetadata()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<HandBoundMetaEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAcceptsMetadata>());
    }

    // The spike in FormBindingHarnessTests proved a null-RequestType IAcceptsMetadata still drives
    // ConsumesMatcherPolicy; this pins that a binder reporting RequestBodyKind.Form makes the tier
    // declare exactly that shape rather than the JSON Accepts every other binder gets.
    [Fact]
    public void CreateDescriptor_ForABinderReportingAFormBody_DeclaresBothFormContentTypesAndNoSchema()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<FormEndpoint>();

        // Assert — no schema is the point: a message holding an IFormFile is not describable as one.
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
        Assert.NotNull(accepts);
        Assert.Null(accepts!.RequestType);
        Assert.Equal(["multipart/form-data", "application/x-www-form-urlencoded"], accepts.ContentTypes);
    }

    internal sealed record FormRequest : IRequest<string>
    {
        // Form-bound rather than empty: the request body an endpoint declares now comes from what its
        // generated binding reads, so the message has to actually read a form field for the tier to
        // declare a form body.
        [FromForm]
        public string Caption { get; init; } = "";
    }

    [Post("/uploads")]
    internal sealed partial class FormEndpoint : Endpoint<FormRequest, string>;

    internal sealed record MetaQuery : IRequest<string>
    {
        // Body-bound (an unannotated property on a body-carrying verb), so the generated binding
        // reads a JSON body and the tier declares one.
        public string Title { get; init; } = "";
    }

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

    [Get("/meta-bodyless-mapper")]
    internal sealed partial class BodylessMapperEndpoint : Endpoint<BodylessMetaQuery, string>
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

    [Get("/meta-created-mapper")]
    internal sealed partial class CreatedMapperEndpoint : Endpoint<BodylessMetaQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.Created(value => $"/things/{value}");
        }
    }

    [Post("/meta")]
    internal sealed partial class MetaEndpoint : Endpoint<MetaQuery, string>;

    internal sealed record BodylessMetaQuery : IRequest<string>;

    [Get("/meta-get")]
    internal sealed partial class BodylessMetaEndpoint : Endpoint<BodylessMetaQuery, string>;

    // OPTIONS and TRACE have no dedicated verb attribute, so they are declared through
    // [HttpEndpoint(...)] — the route these two carry is arbitrary, since each test maps only its own
    // endpoint. One type per verb, because an endpoint's verb is now part of the endpoint.
    [HttpEndpoint("OPTIONS", "/meta-options")]
    internal sealed partial class OptionsMetaEndpoint : Endpoint<BodylessMetaQuery, string>;

    [HttpEndpoint("TRACE", "/meta-trace")]
    internal sealed partial class TraceMetaEndpoint : Endpoint<BodylessMetaQuery, string>;

    internal sealed record CreatedMetaCommand : IRequest<string>;

    [Post("/meta-created")]
    internal sealed partial class CreatedMetaEndpoint : Endpoint<CreatedMetaCommand, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.Created(response => $"/meta-created/{response}");
        }
    }

    internal sealed record CreatedMappedRequest(string Name);

    internal sealed record CreatedMappedCommand(string Name) : IRequest<int>;

    internal sealed record CreatedMappedResponse(string Id);

    [Post("/meta-mapped-created")]
    internal sealed partial class CreatedMappedEndpoint
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

    internal sealed record VoidMetaCommand : IRequest;

    [Post("/meta-void")]
    internal sealed partial class VoidMetaEndpoint : Endpoint<VoidMetaCommand>;

    internal sealed record NoBodyMetaQuery : IRequest<string>;

    [Post("/meta-no-body")]
    internal sealed partial class NoBodyMetaEndpoint : Endpoint<NoBodyMetaQuery, string>;

    internal sealed record HandBoundMetaCommand : IRequest<string>;

    [Post("/meta-hand-bound")]
    internal sealed partial class HandBoundMetaEndpoint : RawEndpoint<HandBoundMetaCommand, string>
    {
        public override ValueTask<BindResult<HandBoundMetaCommand>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(
                BindResult<HandBoundMetaCommand>.Success(new HandBoundMetaCommand()));
        }
    }

    internal sealed record StreamMetaQuery : IStreamRequest<int>;

    [Get("/meta-stream")]
    internal sealed partial class StreamMetaEndpoint : StreamEndpoint<StreamMetaQuery, int>;

    internal sealed record PostStreamMetaQuery : IStreamRequest<int>
    {
        // Body-bound, because the endpoint's verb carries one and nothing else claims the property.
        // That is what makes the generated binding report RequestBodyKind.Json, and so what makes
        // this tier declare an Accepts — the stub this replaced reported Json from a default instead.
        public string Filter { get; init; } = "";
    }

    [Post("/meta-stream-post")]
    internal sealed partial class PostStreamMetaEndpoint : StreamEndpoint<PostStreamMetaQuery, int>;
    // docs/endpoints/features/001: the document listed the success status and the binding 400 and
    // nothing else, so every status the registered IFailureHttpMapper really writes was absent — and
    // the high tier could not add one even by hand, because Produces lived on IRawEndpointBuilder
    // only. Declaring them is now the endpoint's own business, at every tier.
    [Fact]
    public void CreateDescriptor_ForAnEndpointDeclaringAProblemResponse_DeclaresTheProblemBody()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<FailureAwareEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var notFound = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status404NotFound);

        Assert.Equal(typeof(ProblemDetails), notFound.Type);
        Assert.Equal(["application/problem+json"], notFound.ContentTypes);
    }

    [Fact]
    public void CreateDescriptor_ForAnEndpointDeclaringATypedFailureBody_DeclaresThatType()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<FailureAwareEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var conflict = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status409Conflict);

        Assert.Equal(typeof(MetaError), conflict.Type);
        Assert.Equal(["application/json"], conflict.ContentTypes);
    }

    // The regression guard ProducesResponseMetadata exists for: Microsoft.AspNetCore.OpenApi skips an
    // IProducesResponseTypeMetadata whose Type is null outright, so a declared bodyless status has to
    // reach the document as void or it never reaches it at all — see docs/known-issues/051.
    [Fact]
    public void CreateDescriptor_ForADeclaredStatusWithNoBody_DeclaresItAsVoidRatherThanSkippingIt()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<FailureAwareEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var notModified = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status304NotModified);

        Assert.Equal(typeof(void), notModified.Type);
        Assert.Empty(notModified.ContentTypes);
    }

    // A second validation problem at a different status, so the errors dictionary is described rather
    // than the narrower plain ProblemDetails — the same distinction docs/known-issues/055 drew for the
    // binding 400.
    [Fact]
    public void CreateDescriptor_ForADeclaredValidationProblem_DeclaresTheErrorsDictionaryBody()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<FailureAwareEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var unprocessable = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status422UnprocessableEntity);

        Assert.Equal(typeof(HttpValidationProblemDetails), unprocessable.Type);
        Assert.Equal(["application/problem+json"], unprocessable.ContentTypes);
    }

    // Declaring the success status must not be a side effect of declaring a failure one: the endpoint
    // above configures Ok() at the end of the chain and still gets its 200 with the response body.
    [Fact]
    public void CreateDescriptor_ForAnEndpointDeclaringFailureResponses_StillDeclaresTheSuccessResponse()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<FailureAwareEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var success = endpoint.Metadata.OfType<ProducesResponseMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status200OK);

        Assert.Equal(typeof(string), success.Type);
    }

    // The remaining tiers, one test each: the builder surface is per-tier, so "available on every
    // builder interface" is only true if each tier's own Configure can reach it.
    [Fact]
    public void CreateDescriptor_ForAVoidEndpointDeclaringAProblemResponse_DeclaresIt()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<FailureAwareVoidEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var notFound = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status404NotFound);

        Assert.Equal(typeof(ProblemDetails), notFound.Type);
    }

    [Fact]
    public void CreateDescriptor_ForAMappedEndpointDeclaringAProblemResponse_DeclaresIt()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<FailureAwareMappedEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var notFound = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status404NotFound);

        Assert.Equal(typeof(ProblemDetails), notFound.Type);
    }

    [Fact]
    public void CreateDescriptor_ForAHandBoundEndpointDeclaringAProblemResponse_DeclaresIt()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<FailureAwareHandBoundEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var notFound = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status404NotFound);

        Assert.Equal(typeof(ProblemDetails), notFound.Type);
    }

    [Fact]
    public void CreateDescriptor_ForAStreamEndpointDeclaringAProblemResponse_DeclaresIt()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<FailureAwareStreamEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var notFound = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status404NotFound);

        Assert.Equal(typeof(ProblemDetails), notFound.Type);
    }

    // The low tier already had Produces; it lacked the problem shorthands, so an endpoint that
    // answers 404 had to spell out ProblemDetails and its content type by hand.
    [Fact]
    public void CreateDescriptor_ForARawEndpointDeclaringProblemResponses_DeclaresThem()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<FailureAwareRawEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        var produces = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().ToArray();

        Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status404NotFound &&
                                              metadata.Type == typeof(ProblemDetails));
        Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status422UnprocessableEntity &&
                                              metadata.Type == typeof(HttpValidationProblemDetails));
    }

    private sealed record MetaError(string Code);

    // Chained on purpose: the whole chain compiles only while every declaration returns
    // IEndpointBuilder<string> rather than widening to the non-generic IEndpointBuilder, which is what
    // lets the response-shaping Ok() at the end still be in reach.
    [Get("/meta-failure")]
    internal sealed partial class FailureAwareEndpoint : Endpoint<BodylessMetaQuery, string>
    {
        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.ProducesProblem(StatusCodes.Status404NotFound)
                   .Produces<MetaError>(StatusCodes.Status409Conflict)
                   .Produces(StatusCodes.Status304NotModified)
                   .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
                   .Ok();
        }
    }

    [Post("/meta-failure-void")]
    internal sealed partial class FailureAwareVoidEndpoint : Endpoint<VoidMetaCommand>
    {
        public override void Configure(IEndpointBuilder builder)
        {
            builder.ProducesProblem(StatusCodes.Status404NotFound)
                   .NoContent();
        }
    }

    [Post("/meta-failure-mapped")]
    internal sealed partial class FailureAwareMappedEndpoint
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
            builder.ProducesProblem(StatusCodes.Status404NotFound);
        }
    }

    [Post("/meta-failure-hand-bound")]
    internal sealed partial class FailureAwareHandBoundEndpoint : RawEndpoint<HandBoundMetaCommand, string>
    {
        public override ValueTask<BindResult<HandBoundMetaCommand>> BindAsync(HttpContext context)
        {
            return ValueTask.FromResult(
                BindResult<HandBoundMetaCommand>.Success(new HandBoundMetaCommand()));
        }

        public override void Configure(IEndpointBuilder<string> builder)
        {
            builder.ProducesProblem(StatusCodes.Status404NotFound);
        }
    }

    internal sealed record FailureAwareStreamQuery : IStreamRequest<int>;

    [Get("/meta-failure-stream")]
    internal sealed partial class FailureAwareStreamEndpoint : StreamEndpoint<FailureAwareStreamQuery, int>
    {
        public override void Configure(IStreamEndpointBuilder builder)
        {
            builder.ProducesProblem(StatusCodes.Status404NotFound);
        }
    }

    [Get("/meta-failure-raw")]
    internal sealed partial class FailureAwareRawEndpoint : RawEndpoint
    {
        public override void Configure(IRawEndpointBuilder builder)
        {
            builder.ProducesProblem(StatusCodes.Status404NotFound)
                   .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
        }

        public override ValueTask<IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(TypedResults.NoContent() as IResult);
        }
    }

    internal sealed record SelfHandledMetaRequest(string Name);

    [Post("/meta-self-handled")]
    internal sealed partial class SelfHandledMetaEndpoint : SelfHandledEndpoint<SelfHandledMetaRequest, string>
    {
        public override ValueTask<UnambitiousFx.Functional.Result<string>> ExecuteAsync(
            SelfHandledMetaRequest request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(UnambitiousFx.Functional.Result.Success(request.Name));
        }
    }

    internal sealed record SelfHandledVoidMetaRequest(string Name);

    [Post("/meta-self-handled-void")]
    internal sealed partial class SelfHandledVoidMetaEndpoint : SelfHandledEndpoint<SelfHandledVoidMetaRequest>
    {
        public override ValueTask<UnambitiousFx.Functional.Result> ExecuteAsync(
            SelfHandledVoidMetaRequest request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(UnambitiousFx.Functional.Result.Success());
        }
    }

    [Fact]
    public void FormRequestMetadata_WithFields_ExposesThemAndKeepsBothContentTypes()
    {
        // Arrange
        var caption = new FormFieldMetadata
        {
            Name = "Caption",
            Required = true,
            IsArray = false,
            ValueType = typeof(string)
        };

        // Act
        var metadata = new FormRequestMetadata([caption]);

        // Assert
        Assert.Single(metadata.Fields);
        Assert.Equal("Caption", metadata.Fields[0].Name);

        // The content types are what ConsumesMatcherPolicy needs to answer 415; adding a schema
        // must not disturb them.
        Assert.Equal(
            ["multipart/form-data", "application/x-www-form-urlencoded"],
            metadata.ContentTypes);

        // Still null: a message holding an IFormFile has no JSON schema to describe.
        Assert.Null(metadata.RequestType);
        Assert.False(metadata.IsOptional);
    }

    [Fact]
    public void FormRequestMetadata_WithNoFields_IsStillValid()
    {
        // Arrange & Act — the shape every caller passes until Task 5 emits real fields.
        var metadata = new FormRequestMetadata([]);

        // Assert
        Assert.Empty(metadata.Fields);
        Assert.Equal(2, metadata.ContentTypes.Count);
    }

    internal sealed record FormProbeCommand : IRequest<string>;

    /// <summary>
    ///     Declares a form body through the hook a generated binding overrides. Exercises the verb
    ///     narrowing in its single home on RawEndpoint rather than the copies that used to sit on both
    ///     Endpoint arities. Abstract, so the two verbs below can share it: discovery skips abstract
    ///     classes, so it declares no route of its own.
    /// </summary>
    internal abstract partial class FormProbeEndpointBase : RawEndpoint<FormProbeCommand, string>
    {
        public override ValueTask<BindResult<FormProbeCommand>> BindAsync(HttpContext context)
        {
            return new(BindResult<FormProbeCommand>.Success(new FormProbeCommand()));
        }

        protected override RequestBodyKind BoundBodyKind => RequestBodyKind.Form;
    }

    [Post("/form-probe-post")]
    internal sealed partial class PostFormProbeEndpoint : FormProbeEndpointBase;

    [Get("/form-probe-get")]
    internal sealed partial class GetFormProbeEndpoint : FormProbeEndpointBase;

    // Two facts rather than a theory over the verb: the verb is declared on the endpoint type, so it
    // cannot come from an [InlineData].
    [Fact]
    public void CreateDescriptor_WithFormBoundBodyKindOnPost_DeclaresAFormBody()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<PostFormProbeEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        Assert.NotNull(endpoint.Metadata.GetMetadata<FormRequestMetadata>());
    }

    [Fact]
    public void CreateDescriptor_WithFormBoundBodyKindOnGet_DeclaresNoFormBody()
    {
        // Arrange
        var app = WebApplication.CreateSlimBuilder().Build();

        // Act
        app.MapEndpoint<GetFormProbeEndpoint>();

        // Assert
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Single();
        Assert.Null(endpoint.Metadata.GetMetadata<FormRequestMetadata>());
    }
}
