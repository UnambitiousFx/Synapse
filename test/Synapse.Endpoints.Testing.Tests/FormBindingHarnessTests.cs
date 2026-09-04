using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using UnambitiousFx.Functional;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.Endpoints.Binding;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Testing.Tests;

public sealed class FormBindingHarnessTests
{
    [Fact]
    public async Task SendAsync_WhenAnEndpointDeclaresOnlyFormContentTypes_AnswersJsonWith415()
    {
        // Arrange — the whole design rests on a custom IAcceptsMetadata driving the consumes
        // matcher policy. A null RequestType is how we decline to describe a schema we defer;
        // this asserts that declining costs nothing at the matcher.
        EndpointRegistry.RegisterMetadata<ProbeEndpoint>(new EndpointMetadata(["POST"], "/probe"));
        using var harness = EndpointHarness.Create<ProbeEndpoint>();

        // Act
        var response = await harness.Post("/probe")
            .Body("{}", "application/json")
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WhenAnEndpointDeclaresFormContentTypes_AcceptsUrlEncoded()
    {
        // Arrange
        EndpointRegistry.RegisterMetadata<ProbeEndpoint>(new EndpointMetadata(["POST"], "/probe"));
        using var harness = EndpointHarness.Create<ProbeEndpoint>();

        // Act
        var response = await harness.Post("/probe")
            .Body("caption=hello", "application/x-www-form-urlencoded")
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WithAMultipartUpload_BindsTheFileAndTheField()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new UploadBinder());
        EndpointRegistry.RegisterMetadata<UploadEndpoint>(new EndpointMetadata(["POST"], "/uploads"));
        using var harness = EndpointHarness.Create<UploadEndpoint>(options =>
            options.Handle<UploadCommand, string>(command =>
                Result.Success($"{command.File.FileName}:{command.Caption}")));

        // Act
        var response = await harness.Post("/uploads")
            .MultipartBody([("caption", "hello")], [("file", "note.txt", "hi")])
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("note.txt:hello", response.ReadJson<string>());
    }

    [Fact]
    public async Task SendAsync_WithUrlEncodedFields_BindsThemToo()
    {
        // Arrange — both form content types bind, not only multipart.
        EndpointRegistry.RegisterBinder(new CaptionOnlyBinder());
        EndpointRegistry.RegisterMetadata<CaptionEndpoint>(new EndpointMetadata(["POST"], "/captions"));
        using var harness = EndpointHarness.Create<CaptionEndpoint>(options =>
            options.Handle<CaptionCommand, string>(command => Result.Success(command.Caption)));

        // Act
        var response = await harness.Post("/captions")
            .FormBody(("caption", "hello"))
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal("hello", response.ReadJson<string>());
    }

    [Fact]
    public async Task SendAsync_WithAnEmptyMultipartBody_Reports400NamingBothMissingFields()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new UploadBinder());
        EndpointRegistry.RegisterMetadata<UploadEndpoint>(new EndpointMetadata(["POST"], "/uploads"));
        using var harness = EndpointHarness.Create<UploadEndpoint>();

        // Act
        var response = await harness.Post("/uploads")
            .MultipartBody([], [])
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert — one response naming everything wrong, not one round trip per mistake.
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        var problem = response.ReadValidationProblem();
        Assert.Equal(
            ["caption", "file"],
            problem.Errors.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task SendAsync_WithAJsonBodyToAFormEndpoint_Answers415BeforeTheBinderRuns()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new UploadBinder());
        EndpointRegistry.RegisterMetadata<UploadEndpoint>(new EndpointMetadata(["POST"], "/uploads"));
        using var harness = EndpointHarness.Create<UploadEndpoint>();

        // Act
        var response = await harness.Post("/uploads")
            .Body("{}", "application/json")
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert — the consumes matcher, not a 400 from the binder.
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WithOneBadElement_ReportsItsIndexAndStillBindsTheOthers()
    {
        // Arrange
        EndpointRegistry.RegisterBinder(new SearchBinder());
        EndpointRegistry.RegisterMetadata<SearchEndpoint>(new EndpointMetadata(["GET"], "/search"));
        using var harness = EndpointHarness.Create<SearchEndpoint>();

        // Act
        var response = await harness.Get("/search?status=Open&status=Nope")
            .SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Contains("at index 1", Assert.Single(response.ReadValidationProblem().Errors["status"]));
    }

    [Fact]
    public async Task SendAsync_WithNoRepeatedKeyAtAll_BindsAnEmptyCollectionAndSucceeds()
    {
        // Arrange — HTTP cannot send zero values under a key, so absence is not a failure.
        EndpointRegistry.RegisterBinder(new SearchBinder());
        EndpointRegistry.RegisterMetadata<SearchEndpoint>(new EndpointMetadata(["GET"], "/search"));
        using var harness = EndpointHarness.Create<SearchEndpoint>(options =>
            options.Handle<SearchQuery, int>(query => Result.Success(query.Statuses.Length)));

        // Act
        var response = await harness.Get("/search").SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(0, response.ReadJson<int>());
    }

    private sealed class ProbeAcceptsMetadata : IAcceptsMetadata
    {
        public Type? RequestType => null;

        public IReadOnlyList<string> ContentTypes { get; } =
            ["multipart/form-data", "application/x-www-form-urlencoded"];

        public bool IsOptional => false;
    }

    private sealed class ProbeEndpoint : RawEndpoint
    {
        public override void Configure(IRawEndpointBuilder builder)
        {
            builder.Raw(route => route.WithMetadata(new ProbeAcceptsMetadata()));
        }

        public override ValueTask<Microsoft.AspNetCore.Http.IResult> HandleAsync(HttpContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<Microsoft.AspNetCore.Http.IResult>(TypedResults.Ok());
        }
    }

    public sealed record UploadCommand : IRequest<string>
    {
        public required IFormFile File { get; init; }

        public required string Caption { get; init; }
    }

    private sealed class UploadBinder : IEndpointBinder<UploadCommand>
    {
        public RequestBodyKind BodyKind => RequestBodyKind.Form;

        public bool ReadsRequestBody => true;

        public async ValueTask<BindResult<UploadCommand>> BindAsync(HttpContext context)
        {
            var form = await context.FormAsync();
            if (!form.IsSuccess)
            {
                return BindResult<UploadCommand>.Failure(
                    BindingHelpers.BodyField, "The request body could not be read as a form.");
            }

            var validation = context.Validate();
            validation.FormFile("file", out var file);
            validation.Form<string>("caption", out var caption);

            return validation.IsValid
                ? BindResult<UploadCommand>.Success(new UploadCommand { File = file, Caption = caption })
                : BindResult<UploadCommand>.Failure(validation);
        }
    }

    private sealed class UploadEndpoint : Endpoint<UploadCommand, string>;

    // "Same three shapes as Upload*, file dropped" is written out rather than reusing UploadBinder:
    // UploadBinder's FormFile call would report a missing "file" on a fields-only request, which is
    // exactly the failure this shape needs to NOT produce.
    private sealed record CaptionCommand : IRequest<string>
    {
        public required string Caption { get; init; }
    }

    private sealed class CaptionOnlyBinder : IEndpointBinder<CaptionCommand>
    {
        public RequestBodyKind BodyKind => RequestBodyKind.Form;

        public bool ReadsRequestBody => true;

        public async ValueTask<BindResult<CaptionCommand>> BindAsync(HttpContext context)
        {
            var form = await context.FormAsync();
            if (!form.IsSuccess)
            {
                return BindResult<CaptionCommand>.Failure(
                    BindingHelpers.BodyField, "The request body could not be read as a form.");
            }

            var validation = context.Validate();
            validation.Form<string>("caption", out var caption);

            return validation.IsValid
                ? BindResult<CaptionCommand>.Success(new CaptionCommand { Caption = caption })
                : BindResult<CaptionCommand>.Failure(validation);
        }
    }

    private sealed class CaptionEndpoint : Endpoint<CaptionCommand, string>;

    private enum TaskState
    {
        Open,
        Closed
    }

    private sealed record SearchQuery : IRequest<int>
    {
        public required TaskState[] Statuses { get; init; }
    }

    private sealed class SearchBinder : IEndpointBinder<SearchQuery>
    {
        public ValueTask<BindResult<SearchQuery>> BindAsync(HttpContext context)
        {
            var validation = context.Validate();
            validation.QueryValuesEnum<TaskState>("status", out var states);

            return ValueTask.FromResult(validation.IsValid
                ? BindResult<SearchQuery>.Success(new SearchQuery { Statuses = states })
                : BindResult<SearchQuery>.Failure(validation));
        }
    }

    private sealed class SearchEndpoint : Endpoint<SearchQuery, int>;
}
