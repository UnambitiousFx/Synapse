using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using UnambitiousFx.Functional;
using UnambitiousFx.Functional.Failures;
using UnambitiousFx.Synapse.Abstractions;
using UnambitiousFx.Synapse.AspNetCore;
using UnambitiousFx.Synapse.Endpoints.Builders;

namespace UnambitiousFx.Synapse.Endpoints.Tests;

public sealed partial class SelfHandledEndpointTests
{
    [Fact]
    public async Task Invoke_WithRequestThatIsNotAMessage_RunsExecuteAsyncAndWritesItsResponse()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        context.Request.RouteValues["probe"] = "live";

        var endpoint = new ProbeEndpoint();
        var descriptor = ((EndpointBase)endpoint).CreateDescriptor(endpoint.Metadata);

        // Act
        await descriptor.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<ProbeDto>(
            context.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("live", body!.Probe);
    }

    [Fact]
    public async Task Invoke_WhenExecuteAsyncFails_AnswersIdenticallyToTheSameFailureDispatched()
    {
        // Arrange: the same NotFoundFailure, once returned by a self-handled endpoint and once
        // returned by a handler behind Endpoint<TRequest, TResponse>. Both run against the real
        // HttpInvoker and the real DefaultFailureHttpMapper, so any difference is the tier's.

        var mediator = Substitute.For<IInvoker>();
        mediator.InvokeAsync(Arg.Any<MissingProbeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Failure<ProbeDto>(Missing())));

        var services = new ServiceCollection();
        services.AddSingleton(mediator);
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        // Act
        var selfHandled = await InvokeAsync(new MissingProbeEndpoint(),
            new MissingProbeEndpoint().Metadata, provider);
        var dispatched = await InvokeAsync(new DispatchedMissingProbeEndpoint(),
            new DispatchedMissingProbeEndpoint().Metadata, provider);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, selfHandled.StatusCode);
        Assert.Equal(dispatched.StatusCode, selfHandled.StatusCode);
        Assert.Equal(dispatched.ContentType, selfHandled.ContentType);
        Assert.Equal(dispatched.Body, selfHandled.Body);
    }

    [Fact]
    public async Task Invoke_WithConfiguredCreatedMapping_UsesItInsteadOfOnSuccess()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynapseAspNetCore();
        services.AddLogging();

        // Act
        var response = await InvokeAsync(new CreatedProbeEndpoint(),
            new CreatedProbeEndpoint().Metadata, services.BuildServiceProvider(), probe: "live");

        // Assert
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WithOverriddenOnSuccess_WritesTheResultThatOverrideReturns()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynapseAspNetCore();
        services.AddLogging();

        // Act
        var response = await InvokeAsync(new AcceptedProbeEndpoint(),
            new AcceptedProbeEndpoint().Metadata, services.BuildServiceProvider(), probe: "live");

        // Assert
        Assert.Equal(StatusCodes.Status202Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WhenBindingFails_Answers400WithoutRunningExecuteAsync()
    {
        // Arrange — no stub rejects this; the generated binding does. RejectingProbeQuery's Probe has
        // no route parameter to read on GET /probes-rejected, so it binds from the query string and is
        // required there, and the request below sends none.

        var services = new ServiceCollection();
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var endpoint = new RejectingProbeEndpoint();

        // Act
        var response = await InvokeAsync(endpoint,
            new RejectingProbeEndpoint().Metadata, services.BuildServiceProvider());

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.False(endpoint.Ran);
    }

    [Fact]
    public async Task Invoke_WhenOnBeforeHandleAsyncShortCircuits_DoesNotRunExecuteAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSynapseAspNetCore();
        services.AddLogging();
        var endpoint = new ShortCircuitProbeEndpoint();

        // Act
        var response = await InvokeAsync(endpoint,
            new ShortCircuitProbeEndpoint().Metadata, services.BuildServiceProvider(),
            probe: "live");

        // Assert
        Assert.Equal(StatusCodes.Status304NotModified, response.StatusCode);
        Assert.False(endpoint.Ran);
    }

    private static NotFoundFailure Missing()
    {
        return new NotFoundFailure("Probe", "live");
    }

    /// <summary>Invokes one endpoint and returns what it wrote.</summary>
    /// <param name="endpoint">The endpoint under test.</param>
    /// <param name="metadata">Its route metadata.</param>
    /// <param name="provider">The request services.</param>
    /// <param name="probe">
    ///     The <c>probe</c> route value, for the endpoints whose generated binding reads one. Null
    ///     leaves the route empty, which is what the rejecting endpoint's 400 needs.
    /// </param>
    private static async Task<(int StatusCode, string? ContentType, string Body)> InvokeAsync(
        EndpointBase endpoint,
        EndpointMetadata metadata,
        IServiceProvider provider,
        string? probe = null)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = new MemoryStream();

        if (probe is not null)
        {
            context.Request.RouteValues["probe"] = probe;
        }

        await endpoint.CreateDescriptor(metadata).InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        return (context.Response.StatusCode, context.Response.ContentType, body);
    }

    internal sealed record ProbeQuery(string Probe);

    internal sealed record ProbeDto(string Probe);

    [Get("/health/{probe}")]
    internal sealed partial class ProbeEndpoint : SelfHandledEndpoint<ProbeQuery, ProbeDto>
    {
        public override ValueTask<Result<ProbeDto>> ExecuteAsync(ProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe)));
        }
    }

    // A constructor default, for the same reason MissingProbeCommand has one: the generated binding
    // has no route or query value to read on GET /missing, and the failure mapping is the subject.
    internal sealed record MissingProbeQuery(string Probe = "live");

    // A constructor default, so the generated binding on the dispatching endpoint succeeds with no
    // request input at all: the value is irrelevant here, the failure mapping is the subject.
    internal sealed record MissingProbeCommand(string Probe = "live") : IRequest<ProbeDto>;

    [Get("/missing")]
    internal sealed partial class MissingProbeEndpoint : SelfHandledEndpoint<MissingProbeQuery, ProbeDto>
    {
        public override ValueTask<Result<ProbeDto>> ExecuteAsync(MissingProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Failure<ProbeDto>(Missing()));
        }
    }

    [Get("/missing-dispatched")]
    internal sealed partial class DispatchedMissingProbeEndpoint : Endpoint<MissingProbeCommand, ProbeDto>;

    internal sealed record CreatedProbeQuery(string Probe);

    [Post("/probes/{probe}")]
    internal sealed partial class CreatedProbeEndpoint : SelfHandledEndpoint<CreatedProbeQuery, ProbeDto>
    {
        public override void Configure(IEndpointBuilder<ProbeDto> builder)
        {
            builder.Created(response => $"/probes/{response.Probe}");
        }

        public override ValueTask<Result<ProbeDto>> ExecuteAsync(CreatedProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe)));
        }
    }

    internal sealed record AcceptedProbeQuery(string Probe);

    [Post("/probes-accepted/{probe}")]
    internal sealed partial class AcceptedProbeEndpoint : SelfHandledEndpoint<AcceptedProbeQuery, ProbeDto>
    {
        public override Microsoft.AspNetCore.Http.IResult OnSuccess(ProbeDto response,
            HttpContext context)
        {
            return TypedResults.Accepted($"/probes/{response.Probe}", response);
        }

        public override ValueTask<Result<ProbeDto>> ExecuteAsync(AcceptedProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe)));
        }
    }

    internal sealed record RejectingProbeQuery(string Probe);

    [Get("/probes-rejected")]
    internal sealed partial class RejectingProbeEndpoint : SelfHandledEndpoint<RejectingProbeQuery, ProbeDto>
    {
        public bool Ran { get; private set; }

        public override ValueTask<Result<ProbeDto>> ExecuteAsync(RejectingProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            Ran = true;
            return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe)));
        }
    }

    internal sealed record ShortCircuitProbeQuery(string Probe);

    [Get("/probes-short-circuit/{probe}")]
    internal sealed partial class ShortCircuitProbeEndpoint : SelfHandledEndpoint<ShortCircuitProbeQuery, ProbeDto>
    {
        public bool Ran { get; private set; }

        protected override ValueTask<Microsoft.AspNetCore.Http.IResult?> OnBeforeHandleAsync(
            ShortCircuitProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<Microsoft.AspNetCore.Http.IResult?>(
                TypedResults.StatusCode(StatusCodes.Status304NotModified));
        }

        public override ValueTask<Result<ProbeDto>> ExecuteAsync(ShortCircuitProbeQuery request,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            Ran = true;
            return ValueTask.FromResult(Result.Success(new ProbeDto(request.Probe)));
        }
    }
}
